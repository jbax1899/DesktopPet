using System.Buffers.Binary;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopPet.App.Cloud;

namespace DesktopPet.App.Audio;

public sealed class OpenRouterSttAnalyzer : IAudioSegmentAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly Func<OpenRouterSettings> _settingsProvider;
    private readonly Func<AudioContextSettings> _audioSettingsProvider;
    private readonly Func<TimeSpan>? _requestTimeoutProvider;

    public OpenRouterSttAnalyzer(
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
            return Failure(model, AudioAnalysisStatus.Unavailable, "STT analyzer is not configured.");
        }

        using var timeout = new CancellationTokenSource(
            _requestTimeoutProvider?.Invoke()
                ?? TimeSpan.FromSeconds(_audioSettingsProvider().Normalize().AnalysisTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var wav = EncodeWave(segment.MonoSamples.Span, segment.SampleRate);
            var base64 = Convert.ToBase64String(wav);

            var payload = BuildPayload(settings, model, base64);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://openrouter.ai/api/v1/audio/transcriptions");
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
                    $"STT provider rejected the request ({(int)response.StatusCode}).");
            }

            var result = await JsonSerializer.DeserializeAsync<SttResponse>(
                await response.Content.ReadAsStreamAsync(linked.Token),
                JsonOptions,
                linked.Token);
            var text = result?.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                return Failure(model, AudioAnalysisStatus.InvalidResponse, "STT provider returned an empty response.");
            }

            var transcript = options.MaximumTranscriptCharacters > 0 && text.Length > options.MaximumTranscriptCharacters
                ? text[..options.MaximumTranscriptCharacters]
                : text;

            var analysis = new AudioSemanticAnalysis(
                AudioDetectedKind.Speech,
                transcript,
                1.0);

            return new AudioAnalysisResponse(
                "OpenRouter",
                model,
                AudioAnalysisStatus.Success,
                analysis,
                null);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Failure(model, AudioAnalysisStatus.TimedOut, "STT analysis timed out.");
        }
        catch (OperationCanceledException)
        {
            return Failure(model, AudioAnalysisStatus.Cancelled, "STT analysis cancelled.");
        }
        catch (JsonException)
        {
            return Failure(model, AudioAnalysisStatus.InvalidResponse, "STT provider returned an invalid response.");
        }
        catch (HttpRequestException)
        {
            return Failure(model, AudioAnalysisStatus.Failed, "STT analysis request failed.");
        }
        catch
        {
            return Failure(model, AudioAnalysisStatus.Failed, "STT analysis failed.");
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
        string base64Wav)
    {
        var payload = new Dictionary<string, object>
        {
            ["model"] = model,
            ["input_audio"] = new { data = base64Wav, format = "wav" }
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

    private sealed record SttResponse(
        [property: JsonPropertyName("text")] string? Text);
}
