using System.Buffers.Binary;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopPet.App.Cloud;

namespace DesktopPet.App.Audio;

internal sealed class OpenRouterAudioSegmentAnalyzer : IAudioSegmentAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly object ResponseFormat = JsonSerializer.Deserialize<object>("""
    {
      "type": "json_schema",
      "json_schema": {
        "name": "audio_analysis",
        "strict": true,
        "schema": {
          "type": "object",
          "properties": {
            "detected_kind": { "type": "string", "enum": ["speech", "music", "silence", "noise", "mixed", "unknown"] },
            "transcript": { "type": ["string", "null"] },
            "summary": { "type": "string" },
            "event_labels": { "type": "array", "items": { "type": "string" } },
            "confidence": { "type": "number" },
            "sensitivity": { "type": "string", "enum": ["normal", "private_conversation", "sensitive", "unknown"] },
            "should_store": { "type": "boolean" },
            "policy_signals": {
              "type": ["object", "null"],
              "properties": {
                "novelty": { "type": "number" },
                "relevance": { "type": "number" },
                "interruption_cost": { "type": "number" },
                "provider_suggested_commentary": { "type": "boolean" }
              },
              "required": ["novelty", "relevance", "interruption_cost", "provider_suggested_commentary"],
              "additionalProperties": false
            }
          },
          "required": ["detected_kind", "transcript", "summary", "event_labels", "confidence", "sensitivity", "should_store", "policy_signals"],
          "additionalProperties": false
        }
      }
    }
    """)!;

    private readonly HttpClient _httpClient;
    private readonly Func<OpenRouterSettings> _settingsProvider;
    private readonly Func<AudioContextSettings> _audioSettingsProvider;
    private readonly Func<TimeSpan>? _requestTimeoutProvider;

    public OpenRouterAudioSegmentAnalyzer(
        HttpClient httpClient,
        Func<OpenRouterSettings> settingsProvider,
        Func<AudioContextSettings> audioSettingsProvider,
        Func<TimeSpan>? requestTimeoutProvider = null)
    {
        _httpClient = httpClient;
        _settingsProvider = settingsProvider;
        _audioSettingsProvider = audioSettingsProvider;
        _requestTimeoutProvider = requestTimeoutProvider;
    }

    public bool IsAvailable
    {
        get
        {
            var settings = _settingsProvider();
            return !string.IsNullOrWhiteSpace(settings.ApiKey)
                && !string.IsNullOrWhiteSpace(settings.AudioAnalysisModelId);
        }
    }

    public async Task<AudioAnalysisResponse> AnalyzeAsync(
        CompletedAudioSegment segment,
        AudioAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        var settings = _settingsProvider();
        var model = settings.AudioAnalysisModelId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(model))
        {
            return Failure(model, AudioAnalysisStatus.Unavailable, "Audio analyzer is not configured.");
        }

        using var timeout = new CancellationTokenSource(
            _requestTimeoutProvider?.Invoke()
                ?? TimeSpan.FromSeconds(_audioSettingsProvider().Normalize().AnalysisTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var wav = EncodeWave(segment.MonoSamples.Span, segment.SampleRate);
            var base64 = Convert.ToBase64String(wav);
            Array.Clear(wav);

            var payload = BuildPayload(settings, model, base64, options);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://openrouter.ai/api/v1/chat/completions");
            request.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                return Failure(
                    model,
                    AudioAnalysisStatus.ProviderRejected,
                    $"Audio provider rejected the request ({(int)response.StatusCode}).");
            }

            var completion = await JsonSerializer.DeserializeAsync<CompletionResponse>(
                await response.Content.ReadAsStreamAsync(linked.Token),
                JsonOptions,
                linked.Token);
            var content = completion?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return Failure(model, AudioAnalysisStatus.InvalidResponse, "Audio provider returned an empty response.");
            }

            var wire = JsonSerializer.Deserialize<WireAnalysis>(content, JsonOptions);
            if (wire is null || string.IsNullOrWhiteSpace(wire.Summary))
            {
                return Failure(model, AudioAnalysisStatus.InvalidResponse, "Audio provider returned an invalid response.");
            }

            var analysis = new AudioSemanticAnalysis(
                ParseDetectedKind(wire.DetectedKind),
                options.RequestTranscript
                    ? LimitNullable(wire.Transcript, options.MaximumTranscriptCharacters)
                    : null,
                Limit(wire.Summary, options.MaximumSummaryCharacters),
                (wire.EventLabels ?? [])
                    .Where(label => !string.IsNullOrWhiteSpace(label))
                    .Take(options.MaximumEventLabels)
                    .Select(label => Limit(label.Trim(), 80))
                    .ToArray(),
                Clamp(wire.Confidence),
                ParseSensitivity(wire.Sensitivity),
                wire.ShouldStore,
                wire.PolicySignals is null
                    ? null
                    : new AudioPolicySignals(
                        Clamp(wire.PolicySignals.Novelty),
                        Clamp(wire.PolicySignals.Relevance),
                        Clamp(wire.PolicySignals.InterruptionCost),
                        wire.PolicySignals.ProviderSuggestedCommentary));

            return new AudioAnalysisResponse(
                "OpenRouter",
                model,
                AudioAnalysisStatus.Success,
                analysis,
                null);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Failure(model, AudioAnalysisStatus.TimedOut, "Audio analysis timed out.");
        }
        catch (OperationCanceledException)
        {
            return Failure(model, AudioAnalysisStatus.Cancelled, "Audio analysis cancelled.");
        }
        catch (JsonException)
        {
            return Failure(model, AudioAnalysisStatus.InvalidResponse, "Audio provider returned an invalid response.");
        }
        catch (HttpRequestException)
        {
            return Failure(model, AudioAnalysisStatus.Failed, "Audio analysis request failed.");
        }
        catch
        {
            return Failure(model, AudioAnalysisStatus.Failed, "Audio analysis failed.");
        }
    }

    internal static byte[] EncodeWave(ReadOnlySpan<float> samples, int sampleRate)
    {
        var dataLength = checked(samples.Length * sizeof(short));
        var result = new byte[44 + dataLength];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(result, 0);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), 36 + dataLength);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(result, 8);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(28), sampleRate * sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(32), sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(34), 16);
        Encoding.ASCII.GetBytes("data").CopyTo(result, 36);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(40), dataLength);

        for (var i = 0; i < samples.Length; i++)
        {
            var clamped = Math.Clamp(samples[i], -1f, 1f);
            var pcm = clamped <= -1f
                ? short.MinValue
                : (short)Math.Round(clamped * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(44 + (i * 2)), pcm);
        }

        return result;
    }

    internal static object BuildPayload(
        OpenRouterSettings settings,
        string model,
        string base64Wav,
        AudioAnalysisOptions options)
    {
        var prompt = options.TranscriptDetail switch
        {
            AudioTranscriptDetail.Brief =>
                "Analyze this audio segment. Return a concise event summary. Do not include a transcript.",
            AudioTranscriptDetail.Transcript =>
                "Analyze this audio segment. Include a detailed verbatim transcript when speech is intelligible, preserving useful wording while omitting filler only when necessary.",
            _ =>
                "Analyze this audio segment. Include a concise transcript when speech is intelligible and summarize the important meaning."
        };
        var payload = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = new object[]
            {
                new
                {
                    role = "system",
                    content = "Return only the requested structured audio analysis. Be conservative about privacy and storage."
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = prompt },
                        new
                        {
                            type = "input_audio",
                            input_audio = new { data = base64Wav, format = "wav" }
                        }
                    }
                }
            },
            ["response_format"] = ResponseFormat
        };

        if (settings.RequireZeroRetention)
        {
            payload["provider"] = new { zdr = true };
        }

        return payload;
    }

    private static AudioAnalysisResponse Failure(
        string model,
        AudioAnalysisStatus status,
        string message) =>
        new("OpenRouter", model, status, null, new AudioAnalysisFailure(message));

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);

    private static string Limit(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    private static string? LimitNullable(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || maximum <= 0)
        {
            return null;
        }

        return Limit(value, maximum);
    }

    private static AudioDetectedKind ParseDetectedKind(string? value) => value switch
    {
        "speech" => AudioDetectedKind.Speech,
        "music" => AudioDetectedKind.Music,
        "silence" => AudioDetectedKind.Silence,
        "noise" => AudioDetectedKind.Noise,
        "mixed" => AudioDetectedKind.Mixed,
        _ => AudioDetectedKind.Unknown
    };

    private static AudioSensitivity ParseSensitivity(string? value) => value switch
    {
        "normal" => AudioSensitivity.Normal,
        "private_conversation" => AudioSensitivity.PrivateConversation,
        "sensitive" => AudioSensitivity.Sensitive,
        _ => AudioSensitivity.Unknown
    };

    private sealed record CompletionResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<Choice>? Choices);

    private sealed record Choice(
        [property: JsonPropertyName("message")] Message? Message);

    private sealed record Message(
        [property: JsonPropertyName("content")] string? Content);

    private sealed record WireAnalysis(
        [property: JsonPropertyName("detected_kind")] string? DetectedKind,
        [property: JsonPropertyName("transcript")] string? Transcript,
        [property: JsonPropertyName("summary")] string Summary,
        [property: JsonPropertyName("event_labels")] IReadOnlyList<string>? EventLabels,
        [property: JsonPropertyName("confidence")] double Confidence,
        [property: JsonPropertyName("sensitivity")] string? Sensitivity,
        [property: JsonPropertyName("should_store")] bool ShouldStore,
        [property: JsonPropertyName("policy_signals")] WirePolicySignals? PolicySignals);

    private sealed record WirePolicySignals(
        [property: JsonPropertyName("novelty")] double Novelty,
        [property: JsonPropertyName("relevance")] double Relevance,
        [property: JsonPropertyName("interruption_cost")] double InterruptionCost,
        [property: JsonPropertyName("provider_suggested_commentary")] bool ProviderSuggestedCommentary);
}
