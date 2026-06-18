using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopPet.App.Errors;
using DesktopPet.App.Memory;
using DesktopPet.App.Observation;
using DesktopPet.App.Settings;

namespace DesktopPet.App.Cloud;

public sealed class ElevenLabsAgentChatService : IChatService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AgentResponseIdleTimeout = TimeSpan.FromSeconds(3);

    private readonly HttpClient _httpClient;
    private readonly Func<ElevenLabsSettings> _settingsProvider;
    private readonly Func<UiSettings> _uiSettingsProvider;

    public ElevenLabsAgentChatService(
        HttpClient httpClient,
        Func<ElevenLabsSettings> settingsProvider,
        Func<UiSettings> uiSettingsProvider)
    {
        _httpClient = httpClient;
        _settingsProvider = settingsProvider;
        _uiSettingsProvider = uiSettingsProvider;
    }

    public async Task<ChatReply> ReplyAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        var settings = _settingsProvider();
        if (string.IsNullOrWhiteSpace(settings.ElevenLabsApiKey))
        {
            throw new PetErrorException(PetErrorCode.MissingApiKey, "ElevenLabs API key is missing. Add it in Settings first.");
        }

        if (string.IsNullOrWhiteSpace(settings.ElevenLabsAgentId))
        {
            throw new PetErrorException(PetErrorCode.MissingAgentId, "ElevenLabs Agent ID is missing. Add it in Settings first.");
        }

        if (string.IsNullOrWhiteSpace(request.UserMessage))
        {
            throw new PetErrorException(PetErrorCode.ChatFailed, "Type a message before sending.");
        }

        using var timeout = new CancellationTokenSource(ReplyTimeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var signedUrl = await GetSignedUrlAsync(settings, linkedCancellation.Token);
            var replyText = await SendMessageAsync(signedUrl, request, linkedCancellation.Token);
            return new ChatReply(replyText);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new PetErrorException(PetErrorCode.ChatTimeout, "ElevenLabs Agent did not finish replying within 60 seconds.");
        }
    }

    private async Task<string> GetSignedUrlAsync(ElevenLabsSettings settings, CancellationToken cancellationToken)
    {
        var escapedAgentId = Uri.EscapeDataString(settings.ElevenLabsAgentId!);
        Debug.WriteLine("ElevenLabs signed URL requested.");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.elevenlabs.io/v1/convai/conversation/get-signed-url?agent_id={escapedAgentId}");

        request.Headers.Add("xi-api-key", settings.ElevenLabsApiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new PetErrorException(PetErrorCode.ChatFailed, $"ElevenLabs Agent request failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        var signedUrlResponse = await response.Content.ReadFromJsonAsync<SignedUrlResponse>(JsonOptions, cancellationToken);
        if (string.IsNullOrWhiteSpace(signedUrlResponse?.SignedUrl))
        {
            throw new PetErrorException(PetErrorCode.ChatFailed, "ElevenLabs did not return a signed Agent URL.");
        }

        return signedUrlResponse.SignedUrl;
    }

    private async Task<string> SendMessageAsync(string signedUrl, ChatRequest request, CancellationToken cancellationToken)
    {
        using var webSocket = new ClientWebSocket();
        await webSocket.ConnectAsync(new Uri(signedUrl), cancellationToken);

        var initiationData = new Dictionary<string, object?>
        {
            ["type"] = "conversation_initiation_client_data",
            ["conversation_config_override"] = new
            {
                conversation = new
                {
                    text_only = true
                }
            }
        };

        var dynamicVariables = BuildDynamicVariables(request);
        if (dynamicVariables.Count > 0)
        {
            initiationData["dynamic_variables"] = dynamicVariables;
        }

        Debug.WriteLine(dynamicVariables.Count == 0
            ? "ElevenLabs WebSocket initiation has no dynamic variables."
            : $"ElevenLabs WebSocket initiation includes dynamic variables: {string.Join(", ", dynamicVariables.Keys)}");
        await SendJsonAsync(webSocket, initiationData, cancellationToken);

        await SendJsonAsync(webSocket, new
        {
            type = "user_message",
            text = request.UserMessage
        }, cancellationToken);

        var latestResponse = string.Empty;
        var lastAgentResponseAt = (DateTimeOffset?)null;

        while (webSocket.State == WebSocketState.Open)
        {
            var remainingIdleTime = GetRemainingIdleTime(lastAgentResponseAt);
            if (remainingIdleTime <= TimeSpan.Zero)
            {
                Debug.WriteLine("ElevenLabs WebSocket received no further agent response before chat idle timeout; returning latest response.");
                await CloseAsync(webSocket, cancellationToken);
                return latestResponse.Trim();
            }

            using var responseIdleTimeout = remainingIdleTime is null
                ? null
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            if (remainingIdleTime is not null)
            {
                responseIdleTimeout!.CancelAfter(remainingIdleTime.Value);
            }

            string? message;
            try
            {
                message = await ReceiveTextMessageAsync(webSocket, responseIdleTimeout?.Token ?? cancellationToken);
            }
            catch (OperationCanceledException) when (responseIdleTimeout?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
            {
                Debug.WriteLine("ElevenLabs WebSocket received no further agent response before chat idle timeout; returning latest response.");
                await CloseAsync(webSocket, cancellationToken);
                return latestResponse.Trim();
            }

            if (message is null)
            {
                break;
            }

            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            TraceIncomingWebSocketMessage(root);

            if (!root.TryGetProperty("type", out var typeElement))
            {
                continue;
            }

            switch (typeElement.GetString())
            {
                case "ping":
                    await ReplyToPingAsync(webSocket, root, cancellationToken);
                    break;

                case "agent_response":
                    latestResponse = ReadNestedString(root, "agent_response_event", "agent_response") ?? latestResponse;
                    lastAgentResponseAt = DateTimeOffset.UtcNow;
                    break;

                case "agent_response_correction":
                    latestResponse = ReadNestedString(root, "agent_response_correction_event", "corrected_agent_response") ?? latestResponse;
                    lastAgentResponseAt = DateTimeOffset.UtcNow;
                    break;

                case "agent_response_complete":
                    if (string.IsNullOrWhiteSpace(latestResponse))
                    {
                        throw new PetErrorException(PetErrorCode.ChatFailed, "ElevenLabs Agent returned an empty reply.");
                    }

                    await CloseAsync(webSocket, cancellationToken);
                    return latestResponse.Trim();
            }
        }

        throw new PetErrorException(PetErrorCode.ChatFailed, "ElevenLabs Agent disconnected before returning a reply.");
    }

    private static TimeSpan? GetRemainingIdleTime(DateTimeOffset? lastAgentResponseAt)
    {
        if (lastAgentResponseAt is null)
        {
            return null;
        }

        var elapsed = DateTimeOffset.UtcNow - lastAgentResponseAt.Value;
        return AgentResponseIdleTimeout - elapsed;
    }

    private Dictionary<string, string> BuildDynamicVariables(ChatRequest request)
    {
        var now = DateTimeOffset.UtcNow;
        var localTimeZone = TimeZoneInfo.Local;
        var dynamicVariables = new Dictionary<string, string>
        {
            ["temporal_context"] = TemporalContextFormatter.Format(now, localTimeZone)
        };

        if (!string.IsNullOrWhiteSpace(request.MemoriesContext))
        {
            dynamicVariables["memories_context"] = request.MemoriesContext;
        }

        var desktopContext = DesktopContextFormatter.Format(request.DesktopContext);
        if (!string.IsNullOrWhiteSpace(desktopContext))
        {
            dynamicVariables["desktop_context"] = desktopContext;
        }

        var observationHistory = ObservationHistoryFormatter.Format(request.ObservationHistory);
        if (!string.IsNullOrWhiteSpace(observationHistory))
        {
            dynamicVariables["desktop_observation_history"] = observationHistory;
        }

        var historySettings = _uiSettingsProvider().GetEffectiveChatHistoryContext();
        var conversationHistory = ConversationHistoryFormatter.Format(
            request.ConversationHistory,
            now,
            localTimeZone,
            historySettings.RegularMessageCount,
            historySettings.AmbientMessageCount);
        if (!string.IsNullOrWhiteSpace(conversationHistory))
        {
            dynamicVariables["conversation_history"] = conversationHistory;
        }

        var profile = request.ProfileSettings;
        if (profile is null)
        {
            return dynamicVariables;
        }

        var userName = profile.UserName?.Trim();
        var petName = profile.Nickname?.Trim();

        if (!string.IsNullOrWhiteSpace(userName))
        {
            dynamicVariables["user_name"] = userName;
        }

        if (!string.IsNullOrWhiteSpace(petName))
        {
            dynamicVariables["pet_name"] = petName;
        }

        return dynamicVariables;
    }

    private static void TraceIncomingWebSocketMessage(JsonElement root)
    {
        if (!root.TryGetProperty("type", out var typeElement))
        {
            Debug.WriteLine("ElevenLabs WebSocket received a message without a type.");
            return;
        }

        var type = typeElement.GetString() ?? "<null>";
        var detail = type switch
        {
            "conversation_initiation_metadata" => ReadNestedString(root, "conversation_initiation_metadata_event", "conversation_id"),
            "client_tool_call" => ReadNestedString(root, "client_tool_call", "tool_name"),
            "ping" => ReadNestedNumber(root, "ping_event", "event_id"),
            _ => null
        };

        Debug.WriteLine(string.IsNullOrWhiteSpace(detail)
            ? $"ElevenLabs WebSocket received: {type}"
            : $"ElevenLabs WebSocket received: {type} ({detail})");
    }

    private static async Task ReplyToPingAsync(ClientWebSocket webSocket, JsonElement root, CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("ping_event", out var pingEvent)
            || !pingEvent.TryGetProperty("event_id", out var eventId))
        {
            return;
        }

        await SendJsonAsync(webSocket, new
        {
            type = "pong",
            event_id = eventId.GetInt32()
        }, cancellationToken);
    }

    private static string? ReadNestedString(JsonElement root, string objectName, string propertyName)
    {
        if (!root.TryGetProperty(objectName, out var nested)
            || !nested.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.GetString();
    }

    private static string? ReadNestedNumber(JsonElement root, string objectName, string propertyName)
    {
        if (!root.TryGetProperty(objectName, out var nested)
            || !nested.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number
            ? value.GetRawText()
            : value.ToString();
    }

    private static async Task SendJsonAsync(ClientWebSocket webSocket, object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<string?> ReceiveTextMessageAsync(ClientWebSocket webSocket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await webSocket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }

    private static async Task CloseAsync(ClientWebSocket webSocket, CancellationToken cancellationToken)
    {
        if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
        {
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", cancellationToken);
        }
    }

    private sealed record SignedUrlResponse([property: JsonPropertyName("signed_url")] string SignedUrl);
}
