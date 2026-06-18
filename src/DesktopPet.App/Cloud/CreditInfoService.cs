using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopPet.App.Cloud;

public sealed record ElevenLabsCreditInfo(
    string Tier,
    long CharacterCount,
    long CharacterLimit);

public sealed record OpenRouterCreditInfo(
    decimal? Limit,
    decimal? LimitRemaining,
    decimal Usage);

public sealed class CreditInfoService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;
    private readonly Func<ElevenLabsSettings> _elevenLabsSettingsProvider;
    private readonly Func<OpenRouterSettings> _openRouterSettingsProvider;

    public CreditInfoService(
        HttpClient httpClient,
        Func<ElevenLabsSettings> elevenLabsSettingsProvider,
        Func<OpenRouterSettings> openRouterSettingsProvider)
    {
        _httpClient = httpClient;
        _elevenLabsSettingsProvider = elevenLabsSettingsProvider;
        _openRouterSettingsProvider = openRouterSettingsProvider;
    }

    public async Task<ElevenLabsCreditInfo?> GetElevenLabsCreditsAsync(CancellationToken cancellationToken)
    {
        var settings = _elevenLabsSettingsProvider();
        if (string.IsNullOrWhiteSpace(settings.ElevenLabsApiKey))
        {
            return null;
        }

        using var timeout = new CancellationTokenSource(RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.elevenlabs.io/v1/user/subscription");
            request.Headers.Add("xi-api-key", settings.ElevenLabsApiKey);

            using var response = await _httpClient.SendAsync(request, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var subscription = await response.Content.ReadFromJsonAsync<ElevenLabsSubscriptionResponse>(JsonOptions, linked.Token);
            if (subscription is null)
            {
                return null;
            }

            return new ElevenLabsCreditInfo(
                subscription.Tier ?? "unknown",
                subscription.CharacterCount,
                subscription.CharacterLimit);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<OpenRouterCreditInfo?> GetOpenRouterCreditsAsync(CancellationToken cancellationToken)
    {
        var settings = _openRouterSettingsProvider();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return null;
        }

        using var timeout = new CancellationTokenSource(RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/key");
            request.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");

            using var response = await _httpClient.SendAsync(request, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var keyResponse = await response.Content.ReadFromJsonAsync<OpenRouterKeyResponse>(JsonOptions, linked.Token);
            if (keyResponse?.Data is null)
            {
                return null;
            }

            return new OpenRouterCreditInfo(
                keyResponse.Data.Limit,
                keyResponse.Data.LimitRemaining,
                keyResponse.Data.Usage);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private sealed record ElevenLabsSubscriptionResponse(
        [property: JsonPropertyName("tier")] string? Tier,
        [property: JsonPropertyName("character_count")] long CharacterCount,
        [property: JsonPropertyName("character_limit")] long CharacterLimit);

    private sealed record OpenRouterKeyResponse(
        [property: JsonPropertyName("data")] OpenRouterKeyData? Data);

    private sealed record OpenRouterKeyData(
        [property: JsonPropertyName("limit")] decimal? Limit,
        [property: JsonPropertyName("limit_remaining")] decimal? LimitRemaining,
        [property: JsonPropertyName("usage")] decimal Usage);
}
