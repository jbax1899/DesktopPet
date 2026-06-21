using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopPet.App.Cloud;

namespace DesktopPet.App.Observation;

internal sealed class OpenRouterVisionAnalyzer : IVisualContextAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly Func<OpenRouterSettings> _settingsProvider;
    private readonly IObservationPermissionService? _permissionService;
    private readonly ObservationStore? _observationStore;
    private readonly object _sync = new();
    private DateTimeOffset _lastAnalysisAt = DateTimeOffset.MinValue;

    public OpenRouterVisionAnalyzer(
        HttpClient httpClient,
        Func<OpenRouterSettings> settingsProvider,
        IObservationPermissionService? permissionService = null,
        ObservationStore? observationStore = null)
    {
        _httpClient = httpClient;
        _settingsProvider = settingsProvider;
        _permissionService = permissionService;
        _observationStore = observationStore;
    }

    public bool IsAvailable
    {
        get
        {
            var settings = _settingsProvider();
            return !string.IsNullOrWhiteSpace(settings.ApiKey)
                && !string.IsNullOrWhiteSpace(settings.VisionModelId);
        }
    }

    public async Task<VisualContextSummary> AnalyzeAsync(
        CapturedWindowImage image,
        VisualAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        var settings = _settingsProvider();
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.VisionModelId))
        {
            return new VisualContextSummary(DesktopContextCollectionStatus.Unavailable, null);
        }

        var cooldown = GetCooldown();
        lock (_sync)
        {
            var elapsed = DateTimeOffset.UtcNow - _lastAnalysisAt;
            if (elapsed < cooldown)
            {
                return new VisualContextSummary(DesktopContextCollectionStatus.TimedOut, null);
            }
        }

        var base64Image = EncodeImageToBase64(image.Bitmap);
        var currentSettings = _permissionService?.Current;
        var detailLevel = currentSettings?.VisionDetailLevel ?? 5;
        var verbosityLevel = currentSettings?.VisionVerbosityLevel ?? 5;
        var systemPrompt = BuildSystemPrompt(request);
        var userContent = BuildUserContent(request);

        using var timeout = new CancellationTokenSource(GetRequestTimeout());
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var schemaJson = BuildAnalysisSchema(verbosityLevel);
            var requestPayload = BuildRequestPayload(settings, systemPrompt, userContent, base64Image, schemaJson);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
            httpRequest.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(requestPayload, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(httpRequest, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new VisualContextSummary(DesktopContextCollectionStatus.Unavailable, null);
            }

            var completionResponse = await response.Content.ReadFromJsonAsync<CompletionResponse>(JsonOptions, linked.Token);
            var content = completionResponse?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return new VisualContextSummary(DesktopContextCollectionStatus.Empty, null);
            }

            var observation = JsonSerializer.Deserialize<VisionObservation>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (observation is null)
            {
                return new VisualContextSummary(DesktopContextCollectionStatus.Empty, null);
            }

            lock (_sync)
            {
                _lastAnalysisAt = DateTimeOffset.UtcNow;
            }

            var description = FormatObservation(observation);
            return new VisualContextSummary(DesktopContextCollectionStatus.Available, description);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new VisualContextSummary(DesktopContextCollectionStatus.TimedOut, null);
        }
        catch (JsonException)
        {
            return new VisualContextSummary(DesktopContextCollectionStatus.Empty, null);
        }
        catch (HttpRequestException)
        {
            return new VisualContextSummary(DesktopContextCollectionStatus.Unavailable, null);
        }
    }

    public async Task<VisionObservation?> AnalyzeDetailedAsync(
        CapturedWindowImage image,
        VisualAnalysisRequest request,
        IReadOnlyList<ReducedDesktopObservation> recentObservations,
        DateTimeOffset? lastSpokeAt,
        CancellationToken cancellationToken)
    {
        var settings = _settingsProvider();
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.VisionModelId))
        {
            return null;
        }

        var cooldown = GetCooldown();
        lock (_sync)
        {
            var elapsed = DateTimeOffset.UtcNow - _lastAnalysisAt;
            if (elapsed < cooldown)
            {
                return null;
            }
        }

        var base64Image = EncodeImageToBase64(image.Bitmap);
        var currentSettings = _permissionService?.Current;
        var detailLevel = currentSettings?.VisionDetailLevel ?? 5;
        var verbosityLevel = currentSettings?.VisionVerbosityLevel ?? 5;
        var observationHistory = GetObservationHistory();
        var systemPrompt = BuildDetailedSystemPrompt(request, recentObservations, lastSpokeAt, detailLevel, verbosityLevel, observationHistory);

        using var timeout = new CancellationTokenSource(GetRequestTimeout());
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var requestPayload = BuildRequestPayload(settings, systemPrompt, "Analyze this screenshot.", base64Image, BuildAnalysisSchema(verbosityLevel));
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
            httpRequest.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(requestPayload, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(httpRequest, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var completionResponse = await response.Content.ReadFromJsonAsync<CompletionResponse>(JsonOptions, linked.Token);
            var content = completionResponse?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            var observation = JsonSerializer.Deserialize<VisionObservation>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (observation is not null)
            {
                lock (_sync)
                {
                    _lastAnalysisAt = DateTimeOffset.UtcNow;
                }
            }

            return observation;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private TimeSpan GetCooldown()
    {
        var cooldownSeconds = _permissionService?.Current.VisionAnalysisCooldownSeconds ?? 30;
        return TimeSpan.FromSeconds(Math.Max(5, cooldownSeconds));
    }

    private TimeSpan GetRequestTimeout()
    {
        var seconds = _permissionService?.Current.VisionRequestTimeoutSeconds
            ?? ObservationSettings.Default.VisionRequestTimeoutSeconds;
        return TimeSpan.FromSeconds(seconds);
    }

    private IReadOnlyList<ObservationRecord> GetObservationHistory()
    {
        if (_observationStore is null)
        {
            return [];
        }

        try
        {
            return _observationStore.List()
                .OrderByDescending(r => r.CapturedAt)
                .Take(_permissionService?.Current.ObservationContextDepth
                    ?? ObservationSettings.Default.ObservationContextDepth)
                .Reverse()
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static string EncodeImageToBase64(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return Convert.ToBase64String(stream.ToArray());
    }

    private static string BuildSystemPrompt(VisualAnalysisRequest request)
    {
        var parts = new List<string>
        {
            "You are a desktop observation analyzer. Given a screenshot of a permitted application window, produce a structured observation.",
            $"Application: {request.ApplicationName}"
        };

        if (!string.IsNullOrWhiteSpace(request.ActivityDescription))
        {
            parts.Add($"Window title / activity: {request.ActivityDescription}");
        }

        parts.Add("Respond ONLY with valid JSON matching the provided schema.");
        parts.Add("Be concise. Focus on what the user is doing and anything noteworthy.");

        return string.Join(" ", parts);
    }

    private static string BuildDetailedSystemPrompt(
        VisualAnalysisRequest request,
        IReadOnlyList<ReducedDesktopObservation> recentObservations,
        DateTimeOffset? lastSpokeAt,
        int detailLevel,
        int verbosityLevel,
        IReadOnlyList<ObservationRecord> observationHistory)
    {
        var parts = new List<string>
        {
            "You are a desktop observation analyzer for a desktop pet named Pebble.",
            $"Current application: {request.ApplicationName}"
        };

        if (!string.IsNullOrWhiteSpace(request.ActivityDescription))
        {
            parts.Add($"Window title: {request.ActivityDescription}");
        }

        if (recentObservations.Count > 0)
        {
            var previous = recentObservations[^1];
            parts.Add($"Previous observation: {previous.ApplicationName} — {previous.ActivityDescription}");
        }

        if (lastSpokeAt.HasValue)
        {
            var ago = DateTimeOffset.UtcNow - lastSpokeAt.Value;
            parts.Add($"Pebble last spoke: {FormatTimeAgo(ago)} ago");
        }

        if (observationHistory.Count > 0)
        {
            parts.Add("");
            parts.Add("RECENT OBSERVATION HISTORY (most recent last):");
            for (var i = 0; i < observationHistory.Count; i++)
            {
                var obs = observationHistory[i];
                var timeAgo = FormatTimeAgo(DateTimeOffset.UtcNow - obs.CapturedAt);
                parts.Add($"  [{timeAgo} ago] {obs.Analysis.Summary}");
                if (obs.Analysis.NotableChanges.Count > 0)
                {
                    parts.Add($"    Notable: {string.Join("; ", obs.Analysis.NotableChanges.Take(2))}");
                }
            }
            parts.Add("");
            parts.Add("IMPORTANT: Do NOT repeat what has already been observed. Focus on what is NEW, CHANGED, or DIFFERENT since the last observation. If the scene is substantially the same, note what has progressed or evolved.");
        }

        parts.Add(BuildDetailInstruction(detailLevel, verbosityLevel));

        parts.Add("Analyze the screenshot and produce a structured observation.");
        parts.Add("Respond ONLY with valid JSON matching the provided schema.");

        return string.Join(" ", parts);
    }

    private static string FormatTimeAgo(TimeSpan time)
    {
        if (time.TotalMinutes < 1) return "less than a minute";
        if (time.TotalMinutes < 60) return $"{(int)time.TotalMinutes} minutes";
        if (time.TotalHours < 24) return $"{(int)time.TotalHours} hours";
        return $"{(int)time.TotalDays} days";
    }

    private static string BuildDetailInstruction(int detailLevel, int verbosityLevel)
    {
        detailLevel = Math.Clamp(detailLevel, 1, 10);
        verbosityLevel = Math.Clamp(verbosityLevel, 1, 10);

        var detailPart = detailLevel switch
        {
            <= 2 => "Focus only on the user's core action and the single most important element.",
            <= 4 => "Describe what the user is doing and the most notable screen elements.",
            <= 6 => "Describe the user's activity, notable on-screen elements, and visible text labels.",
            <= 8 => "Describe the user's activity, all visible UI elements, text, and layout.",
            _ => "Describe everything visible in full detail — all text, controls, layout, content, and visual elements."
        };

        var verbosityPart = verbosityLevel switch
        {
            <= 2 => "Use one terse sentence. No adjectives or elaboration.",
            <= 4 => "Use brief, direct sentences with minimal description.",
            <= 6 => "Use clear, descriptive sentences.",
            <= 8 => "Use detailed, descriptive language with context and character.",
            _ => "Write a rich, narrative description. Describe the mood, energy, and story of what is happening. Give Pebble something interesting to comment on."
        };

        return $"Produce an observation. {detailPart} {verbosityPart}";
    }

    private static string BuildAnalysisSchema(int verbosityLevel)
    {
        verbosityLevel = Math.Clamp(verbosityLevel, 1, 10);

        var summaryDescription = verbosityLevel switch
        {
            <= 2 => "A single terse sentence describing what is visible",
            <= 4 => "One or two sentences describing what is visible",
            <= 6 => "A short paragraph describing what is visible, with key details",
            <= 8 => "A detailed paragraph describing what is visible, including text, UI elements, and layout",
            _ => "A rich, detailed description of everything visible — text, UI elements, layout, colors, content, and atmosphere"
        };

        return $$"""
        {
          "type": "json_schema",
          "json_schema": {
            "name": "vision_observation",
            "strict": true,
            "schema": {
              "type": "object",
              "properties": {
                "summary": { "type": "string", "description": "{{summaryDescription}}" },
                "visible_activity": { "type": ["string", "null"], "description": "What the user appears to be doing, or null if unclear" },
                "notable_changes": { "type": "array", "items": { "type": "string" }, "description": "Notable things that stand out" },
                "possible_comment_topics": { "type": "array", "items": { "type": "string" }, "description": "Topics a desktop pet might comment on" },
                "novelty": { "type": "number", "description": "How novel this is compared to typical desktop use, 0-1" },
                "relevance": { "type": "number", "description": "How relevant this might be for a companion pet to notice, 0-1" },
                "sensitivity": { "type": "number", "description": "How sensitive/private this content is, 0-1" },
                "interruption_cost": { "type": "number", "description": "How disruptive it would be to interrupt the user now, 0-1" },
                "expires_after_seconds": { "type": "integer", "description": "How long this observation remains relevant in seconds" }
              },
              "required": ["summary", "visible_activity", "notable_changes", "possible_comment_topics", "novelty", "relevance", "sensitivity", "interruption_cost", "expires_after_seconds"],
              "additionalProperties": false
            }
          }
        }
        """;
    }

    private static string BuildUserContent(VisualAnalysisRequest request)
    {
        return $"Analyze this screenshot of {request.ApplicationName}.";
    }

    private static object BuildRequestPayload(OpenRouterSettings settings, string systemPrompt, string userContent, string base64Image, string schemaJson)
    {
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt },
            new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "text", text = userContent },
                    new { type = "image_url", image_url = new { url = $"data:image/png;base64,{base64Image}" } }
                }
            }
        };

        var payload = new Dictionary<string, object>
        {
            ["model"] = settings.VisionModelId!,
            ["messages"] = messages,
            ["response_format"] = JsonSerializer.Deserialize<object>(schemaJson)!
        };

        if (settings.RequireZeroRetention)
        {
            payload["provider"] = new { zdr = true };
        }

        return payload;
    }

    private static string FormatObservation(VisionObservation observation)
    {
        var parts = new List<string> { observation.Summary };

        if (!string.IsNullOrWhiteSpace(observation.VisibleActivity))
        {
            parts.Add($"Activity: {observation.VisibleActivity}");
        }

        if (observation.NotableChanges.Count > 0)
        {
            parts.Add($"Changes: {string.Join("; ", observation.NotableChanges.Take(3))}");
        }

        return string.Join(". ", parts);
    }

    private sealed record CompletionResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<Choice>? Choices);

    private sealed record Choice(
        [property: JsonPropertyName("message")] MessageContent? Message);

    private sealed record MessageContent(
        [property: JsonPropertyName("content")] string? Content);
}
