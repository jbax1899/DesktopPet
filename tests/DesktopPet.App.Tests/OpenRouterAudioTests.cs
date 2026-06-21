using System.Net;
using System.Text;
using System.Text.Json;
using DesktopPet.App.Audio;
using DesktopPet.App.Cloud;

namespace DesktopPet.App.Tests;

[TestClass]
public sealed class OpenRouterAudioTests
{
    [TestMethod]
    public void WaveEncodingProducesMonoPcm16Header()
    {
        var wav = OpenRouterAudioSegmentAnalyzer.EncodeWave([0f, 1f, -1f], 16000);

        Assert.AreEqual("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.AreEqual("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.AreEqual(1, BitConverter.ToInt16(wav, 22));
        Assert.AreEqual(16000, BitConverter.ToInt32(wav, 24));
        Assert.AreEqual(16, BitConverter.ToInt16(wav, 34));
        Assert.AreEqual("data", Encoding.ASCII.GetString(wav, 36, 4));
    }

    [TestMethod]
    public void PayloadContainsAudioSchemaModelAndOptionalZdr()
    {
        var settings = new OpenRouterSettings("key", "vision", "audio/model", true);
        var payload = OpenRouterAudioSegmentAnalyzer.BuildPayload(
            settings,
            "audio/model",
            "UklGRg==",
            new AudioAnalysisOptions(true));
        var json = JsonSerializer.Serialize(payload);

        StringAssert.Contains(json, "\"model\":\"audio/model\"");
        StringAssert.Contains(json, "\"type\":\"input_audio\"");
        StringAssert.Contains(json, "\"input_audio\":{\"data\":\"UklGRg==\",\"format\":\"wav\"}");
        StringAssert.Contains(json, "\"type\":\"json_schema\"");
        StringAssert.Contains(json, "\"zdr\":true");
        StringAssert.Contains(json, "concise transcript");
    }

    [TestMethod]
    public void BriefPayloadExplicitlyDisablesTranscript()
    {
        var payload = OpenRouterAudioSegmentAnalyzer.BuildPayload(
            new OpenRouterSettings("key", "vision", "audio/model", true),
            "audio/model",
            "UklGRg==",
            new AudioAnalysisOptions(
                RequestTranscript: false,
                TranscriptDetail: AudioTranscriptDetail.Brief));
        var json = JsonSerializer.Serialize(payload);

        StringAssert.Contains(json, "Do not include a transcript");
    }

    [TestMethod]
    public async Task StructuredResponseMapsToProviderNeutralAnalysis()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
        {
          "choices": [{
            "message": {
              "content": "{\"detected_kind\":\"speech\",\"transcript\":\"hello\",\"summary\":\"A greeting.\",\"event_labels\":[\"speech\"],\"confidence\":1.4,\"sensitivity\":\"normal\",\"should_store\":true,\"policy_signals\":{\"novelty\":-1,\"relevance\":2,\"interruption_cost\":0.2,\"provider_suggested_commentary\":false}}"
            }
          }]
        }
        """));
        var analyzer = CreateAnalyzer(handler);
        using var segment = CreateSegment();

        var result = await analyzer.AnalyzeAsync(
            segment,
            new AudioAnalysisOptions(true),
            CancellationToken.None);

        Assert.AreEqual(AudioAnalysisStatus.Success, result.Status);
        Assert.AreEqual(AudioDetectedKind.Speech, result.Analysis!.DetectedKind);
        Assert.AreEqual(1, result.Analysis.Confidence);
        Assert.AreEqual(0, result.Analysis.PolicySignals!.Novelty);
        Assert.AreEqual(1, result.Analysis.PolicySignals.Relevance);
    }

    [TestMethod]
    public async Task TranscriptIsBoundedByAnalysisOptions()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
        {
          "choices": [{
            "message": {
              "content": "{\"detected_kind\":\"speech\",\"transcript\":\"abcdefgh\",\"summary\":\"Speech.\",\"event_labels\":[],\"confidence\":0.9,\"sensitivity\":\"normal\",\"should_store\":true,\"policy_signals\":null}"
            }
          }]
        }
        """));
        using var segment = CreateSegment();

        var result = await CreateAnalyzer(handler).AnalyzeAsync(
            segment,
            new AudioAnalysisOptions(
                RequestTranscript: true,
                MaximumTranscriptCharacters: 4),
            CancellationToken.None);

        Assert.AreEqual("abcd", result.Analysis!.Transcript);
    }

    [TestMethod]
    public async Task MalformedEmptyHttpTimeoutAndCancellationMapToExplicitStatuses()
    {
        await AssertStatusAsync(
            new RecordingHandler(_ => JsonResponse("""{"choices":[{"message":{"content":"{"}}]}""")),
            AudioAnalysisStatus.InvalidResponse);
        await AssertStatusAsync(
            new RecordingHandler(_ => JsonResponse("""{"choices":[]}""")),
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
            new AudioAnalysisOptions(true),
            cancellation.Token);
        Assert.AreEqual(AudioAnalysisStatus.Cancelled, cancelled.Status);
    }

    [TestMethod]
    public async Task AudioModelFilteringRequiresAudioAndStructuredOutput()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
        {
          "data": [
            {
              "id": "audio-structured",
              "name": "Audio Structured",
              "architecture": { "input_modalities": ["text", "audio"] },
              "supported_parameters": ["response_format"]
            },
            {
              "id": "audio-unstructured",
              "name": "Audio Unstructured",
              "architecture": { "input_modalities": ["audio"] },
              "supported_parameters": []
            },
            {
              "id": "vision",
              "name": "Vision",
              "architecture": { "input_modalities": ["image"] },
              "supported_parameters": ["response_format"]
            }
          ]
        }
        """));
        var service = new OpenRouterModelsService(
            new HttpClient(handler),
            () => new OpenRouterSettings("key", "vision", "audio-structured", true));

        var models = await service.GetAudioModelsAsync(CancellationToken.None);

        Assert.HasCount(1, models);
        Assert.AreEqual("audio-structured", models[0].Id);
        Assert.IsTrue(models[0].SupportsAudioInput);
        Assert.IsTrue(models[0].SupportsStructuredOutput);
    }

    private static OpenRouterAudioSegmentAnalyzer CreateAnalyzer(
        HttpMessageHandler handler,
        TimeSpan? timeout = null) =>
        new(
            new HttpClient(handler),
            () => new OpenRouterSettings("key", "vision", "audio/model", true),
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
            new AudioAnalysisOptions(true),
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
