using System.Net;
using System.Text;
using System.Text.Json;
using DesktopPet.App.Audio;
using DesktopPet.App.Cloud;

namespace DesktopPet.App.Tests;

[TestClass]
public sealed class OpenRouterSttTests
{
    [TestMethod]
    public void WaveEncodingProducesMonoPcm16Header()
    {
        var wav = OpenRouterSttAnalyzer.EncodeWave([0f, 1f, -1f], 16000);

        Assert.AreEqual("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.AreEqual("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.AreEqual(1, BitConverter.ToInt16(wav, 22));
        Assert.AreEqual(16000, BitConverter.ToInt32(wav, 24));
        Assert.AreEqual(16, BitConverter.ToInt16(wav, 34));
        Assert.AreEqual("data", Encoding.ASCII.GetString(wav, 36, 4));
    }

    [TestMethod]
    public void PayloadContainsModelAudioInputAndOptionalZdr()
    {
        var settings = new OpenRouterSettings("key", "vision", "openai/whisper-large-v3", true);
        var payload = OpenRouterSttAnalyzer.BuildPayload(
            settings,
            "openai/whisper-large-v3",
            "UklGRg==");
        var json = JsonSerializer.Serialize(payload);

        StringAssert.Contains(json, "\"model\":\"openai/whisper-large-v3\"");
        StringAssert.Contains(json, "\"input_audio\":{\"data\":\"UklGRg==\",\"format\":\"wav\"}");
        StringAssert.Contains(json, "\"zdr\":true");
    }

    [TestMethod]
    public void PayloadOmitsZdrWhenDisabled()
    {
        var settings = new OpenRouterSettings("key", "vision", "openai/whisper-large-v3", false);
        var payload = OpenRouterSttAnalyzer.BuildPayload(
            settings,
            "openai/whisper-large-v3",
            "UklGRg==");
        var json = JsonSerializer.Serialize(payload);

        Assert.IsFalse(json.Contains("zdr", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SttResponseMapsToTranscriptAnalysis()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
        {
          "text": "hello world",
          "usage": { "seconds": 2.5, "cost": 0.0005 }
        }
        """));
        var analyzer = CreateAnalyzer(handler);
        using var segment = CreateSegment();

        var result = await analyzer.AnalyzeAsync(
            segment,
            new AudioAnalysisOptions(),
            CancellationToken.None);

        Assert.AreEqual(AudioAnalysisStatus.Success, result.Status);
        Assert.AreEqual("hello world", result.Analysis!.Transcript);
        Assert.AreEqual(AudioDetectedKind.Speech, result.Analysis.DetectedKind);
        Assert.AreEqual(1.0, result.Analysis.Confidence);
    }

    [TestMethod]
    public async Task TranscriptIsBoundedByMaximumCharacters()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
        {
          "text": "abcdefgh"
        }
        """));
        using var segment = CreateSegment();

        var result = await CreateAnalyzer(handler).AnalyzeAsync(
            segment,
            new AudioAnalysisOptions(MaximumTranscriptCharacters: 4),
            CancellationToken.None);

        Assert.AreEqual("abcd", result.Analysis!.Transcript);
    }

    [TestMethod]
    public async Task MalformedEmptyHttpTimeoutAndCancellationMapToExplicitStatuses()
    {
        await AssertStatusAsync(
            new RecordingHandler(_ => JsonResponse("""{"text": null}""")),
            AudioAnalysisStatus.InvalidResponse);
        await AssertStatusAsync(
            new RecordingHandler(_ => JsonResponse("""{}""")),
            AudioAnalysisStatus.InvalidResponse);
        await AssertStatusAsync(
            new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)),
            AudioAnalysisStatus.ProviderRejected);

        var timeoutHandler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return JsonResponse("{}");
        });
        await AssertStatusAsync(timeoutHandler, AudioAnalysisStatus.TimedOut, TimeSpan.FromMilliseconds(20));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var segment = CreateSegment();
        var cancelled = await CreateAnalyzer(timeoutHandler).AnalyzeAsync(
            segment,
            new AudioAnalysisOptions(),
            cancellation.Token);
        Assert.AreEqual(AudioAnalysisStatus.Cancelled, cancelled.Status);
    }

    [TestMethod]
    public async Task EmptyTranscriptReturnsInvalidResponse()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
        {
          "text": ""
        }
        """));
        using var segment = CreateSegment();

        var result = await CreateAnalyzer(handler).AnalyzeAsync(
            segment,
            new AudioAnalysisOptions(),
            CancellationToken.None);

        Assert.AreEqual(AudioAnalysisStatus.InvalidResponse, result.Status);
    }

    [TestMethod]
    public async Task AudioModelFilteringReturnsSttModels()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
        {
          "data": [
            {
              "id": "openai/whisper-large-v3",
              "name": "Whisper Large V3",
              "architecture": { "input_modalities": ["audio"], "output_modalities": ["transcription"] },
              "supported_parameters": ["max_tokens"]
            },
            {
              "id": "openai/gpt-4o",
              "name": "GPT-4o",
              "architecture": { "input_modalities": ["text", "image"], "output_modalities": ["text"] },
              "supported_parameters": ["temperature"]
            }
          ]
        }
        """));
        var service = new OpenRouterModelsService(
            new HttpClient(handler),
            () => new OpenRouterSettings("key", "vision", null, false));

        var models = await service.GetSttModelsAsync(CancellationToken.None);

        Assert.HasCount(1, models);
        Assert.AreEqual("openai/whisper-large-v3", models[0].Id);
    }

    private static OpenRouterSttAnalyzer CreateAnalyzer(
        HttpMessageHandler handler,
        TimeSpan? timeout = null) =>
        new(
            new HttpClient(handler),
            () => new OpenRouterSettings("key", "vision", "openai/whisper-large-v3", false),
            () => AudioContextSettings.Default,
            timeout is null ? null : () => timeout.Value);

    private static CompletedAudioSegment CreateSegment() =>
        new(
            "segment",
            AudioSourceKind.SystemAudio,
            DateTimeOffset.UtcNow.AddSeconds(-1),
            DateTimeOffset.UtcNow,
            16000,
            [0.1f, 0.2f, 0.1f]);

    private static async Task AssertStatusAsync(
        HttpMessageHandler handler,
        AudioAnalysisStatus expected,
        TimeSpan? timeout = null)
    {
        using var segment = CreateSegment();
        var result = await CreateAnalyzer(handler, timeout).AnalyzeAsync(
            segment,
            new AudioAnalysisOptions(),
            CancellationToken.None);
        Assert.AreEqual(expected, result.Status);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _response;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
            : this((request, _) => Task.FromResult(response(request)))
        {
        }

        public RecordingHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            _response(request, cancellationToken);
    }
}
