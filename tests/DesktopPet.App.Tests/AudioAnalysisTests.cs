using System.Text.Json;
using DesktopPet.App.Audio;
using DesktopPet.App.Cloud;
using DesktopPet.App.Memory;
using DesktopPet.App.Settings;

namespace DesktopPet.App.Tests;

[TestClass]
public sealed class AudioAnalysisTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "DesktopPet.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public void CompletedSegmentTransfersSamplesAndDisposeClearsOwnedArray()
    {
        var buffer = new AudioSegmentBuffer(AudioSourceKind.Microphone);
        var now = DateTimeOffset.UtcNow;
        Process(buffer, 0.2f, 500, ref now);
        Process(buffer, 0.2f, 600, ref now);
        var result = Process(buffer, 0, 1500, ref now);

        var segment = result.CompletedSegments.Single();
        var owned = segment.MonoSamples.ToArray();
        Assert.IsGreaterThan(0, segment.MonoSamples.Length);
        Assert.IsTrue(owned.Any(sample => sample != 0));

        segment.Dispose();

        Assert.AreEqual(0, segment.MonoSamples.Length);
    }

    [TestMethod]
    public void RejectedActivityDoesNotProduceCompletedSegment()
    {
        var buffer = new AudioSegmentBuffer(AudioSourceKind.Microphone);
        var now = DateTimeOffset.UtcNow;
        Process(buffer, 0.2f, 200, ref now);
        var result = Process(buffer, 0, 100, ref now);

        Assert.IsEmpty(result.CompletedSegments);
        Assert.AreEqual(
            AudioSegmentDisposition.BelowActivityGate,
            result.Diagnostics.Single().Disposition);
    }

    [TestMethod]
    public async Task CoordinatorRunsSequentiallyAllowsTwoWaitingAndClearsSamples()
    {
        var analyzer = new BlockingAnalyzer();
        var transcript = new TranscriptWorkingBuffer();
        var store = new AudioObservationStore(Path.Combine(_directory, "observations.json"));
        using var coordinator = new AudioAnalysisCoordinator(analyzer, transcript, store);
        coordinator.ApplySettings(EnabledAnalysisSettings());

        var segments = Enumerable.Range(0, 4).Select(_ => CreateSegment()).ToArray();
        Assert.IsTrue(coordinator.TryEnqueue(segments[0]));
        await analyzer.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsTrue(coordinator.TryEnqueue(segments[1]));
        Assert.IsTrue(coordinator.TryEnqueue(segments[2]));
        Assert.IsFalse(coordinator.TryEnqueue(segments[3]));
        segments[3].Dispose();

        analyzer.Release();
        await WaitUntilAsync(() => analyzer.CallCount == 3);
        await WaitUntilAsync(() => !coordinator.Diagnostic.RequestActive);

        Assert.AreEqual(1, analyzer.MaximumConcurrentCalls);
        Assert.AreEqual(1, coordinator.Diagnostic.DroppedCount);
        Assert.IsTrue(segments.Take(3).All(segment => segment.MonoSamples.IsEmpty));
    }

    [TestMethod]
    public async Task CaptureFlowsThroughSegmentationAnalysisAndObservationStorage()
    {
        var analyzer = new SequenceAnalyzer(Success(transcript: "captured speech"));
        var store = new AudioObservationStore(Path.Combine(_directory, "observations.json"));
        using var analysisCoordinator = new AudioAnalysisCoordinator(
            analyzer,
            new TranscriptWorkingBuffer(),
            store);
        var systemAudio = new FakeCaptureSource(AudioSourceKind.SystemAudio);
        using var captureCoordinator = new AudioCaptureCoordinator(
            kind => kind == AudioSourceKind.SystemAudio
                ? systemAudio
                : new FakeCaptureSource(kind),
            analysisCoordinator);

        captureCoordinator.ApplySettings(EnabledAnalysisSettings());
        systemAudio.Emit(0.2f, TimeSpan.FromMilliseconds(1100));
        var end = systemAudio.CapturedAt + TimeSpan.FromSeconds(2);
        for (var now = systemAudio.CapturedAt + TimeSpan.FromMilliseconds(500);
             now <= end;
             now += TimeSpan.FromMilliseconds(500))
        {
            captureCoordinator.ProcessSilenceGaps(now);
        }

        await WaitUntilAsync(() => store.List().Count == 1);

        Assert.AreEqual(AudioSourceKind.SystemAudio, store.List().Single().Source);
        Assert.AreEqual(1, analyzer.CallCount);
    }

    [TestMethod]
    public async Task AnalyzerFailureDoesNotBlockLaterSegment()
    {
        var analyzer = new SequenceAnalyzer(
            Failure(AudioAnalysisStatus.Failed),
            Success(transcript: "second"));
        var transcript = new TranscriptWorkingBuffer();
        var store = new AudioObservationStore(Path.Combine(_directory, "observations.json"));
        using var coordinator = new AudioAnalysisCoordinator(analyzer, transcript, store);
        coordinator.ApplySettings(EnabledAnalysisSettings());

        Assert.IsTrue(coordinator.TryEnqueue(CreateSegment()));
        Assert.IsTrue(coordinator.TryEnqueue(CreateSegment()));
        await WaitUntilAsync(() => analyzer.CallCount == 2);
        await WaitUntilAsync(() => !coordinator.Diagnostic.RequestActive);

        Assert.AreEqual(1, coordinator.Diagnostic.FailureCount);
        Assert.AreEqual(1, coordinator.Diagnostic.SuccessfulCount);
        Assert.HasCount(1, transcript.List());
    }

    [TestMethod]
    public void TranscriptBufferExpiresAndNewInstanceStartsEmpty()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var buffer = new TranscriptWorkingBuffer(() => TimeSpan.FromSeconds(300), clock);
        using var segment = CreateSegment();
        buffer.Add(segment, "temporary transcript", 0.9);

        Assert.HasCount(1, buffer.List());
        clock.Advance(TimeSpan.FromSeconds(300));
        Assert.IsEmpty(buffer.List());
        Assert.IsEmpty(new TranscriptWorkingBuffer().List());
    }

    [TestMethod]
    public void ObservationStoreSupportsRetentionDeleteClearAndChanged()
    {
        var maximum = 2;
        var store = new AudioObservationStore(
            Path.Combine(_directory, "audio-observations.json"),
            () => maximum);
        var changed = 0;
        store.Changed += (_, _) => changed++;

        store.Add(CreateObservation("old", DateTimeOffset.UtcNow.AddMinutes(-2)));
        store.Add(CreateObservation("middle", DateTimeOffset.UtcNow.AddMinutes(-1)));
        store.Add(CreateObservation("new", DateTimeOffset.UtcNow));

        Assert.HasCount(2, store.List());
        Assert.IsFalse(store.List().Any(item => item.Id == "old"));
        Assert.IsTrue(store.Delete("middle"));
        store.Clear();
        Assert.IsEmpty(store.List());
        Assert.AreEqual(5, changed);
    }

    [TestMethod]
    public async Task ObservationFilteringAndExcerptPrivacyRulesAreApplied()
    {
        var analyzer = new SequenceAnalyzer(Success(
            transcript: "private microphone transcript\r\nwith controls",
            confidence: 0.9));
        var storePath = Path.Combine(_directory, "audio-observations.json");
        var store = new AudioObservationStore(storePath);
        using var coordinator = new AudioAnalysisCoordinator(
            analyzer,
            new TranscriptWorkingBuffer(),
            store);
        coordinator.ApplySettings(EnabledAnalysisSettings() with
        {
            PersistMicrophoneTranscriptExcerpt = false
        });

        Assert.IsTrue(coordinator.TryEnqueue(CreateSegment(AudioSourceKind.Microphone)));
        await WaitUntilAsync(() => !coordinator.Diagnostic.RequestActive);

        Assert.IsNull(store.List().Single().TranscriptExcerpt);
        var json = File.ReadAllText(storePath);
        Assert.IsFalse(json.Contains("private microphone transcript", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("MonoSamples", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("PolicySignals", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SystemExcerptDefaultsOnAndLowConfidenceOutputIsNotRetained()
    {
        var analyzer = new SequenceAnalyzer(
            Success(transcript: "system transcript", confidence: 0.9),
            Success(transcript: "uncertain transcript", confidence: 0.59));
        var transcript = new TranscriptWorkingBuffer();
        var store = new AudioObservationStore(Path.Combine(_directory, "audio-observations.json"));
        using var coordinator = new AudioAnalysisCoordinator(analyzer, transcript, store);
        coordinator.ApplySettings(EnabledAnalysisSettings());

        Assert.IsTrue(coordinator.TryEnqueue(CreateSegment()));
        Assert.IsTrue(coordinator.TryEnqueue(CreateSegment()));
        await WaitUntilAsync(() => !coordinator.Diagnostic.RequestActive);

        Assert.HasCount(1, store.List());
        Assert.AreEqual("system transcript", store.List().Single().TranscriptExcerpt);
        Assert.HasCount(1, transcript.List());
    }

    [TestMethod]
    public async Task DisablingCancelsActiveAnalysisDropsQueueAndClearsTranscript()
    {
        var analyzer = new BlockingAnalyzer();
        var transcript = new TranscriptWorkingBuffer();
        using (var existing = CreateSegment())
        {
            transcript.Add(existing, "temporary", 0.9);
        }

        var store = new AudioObservationStore(Path.Combine(_directory, "observations.json"));
        using var coordinator = new AudioAnalysisCoordinator(analyzer, transcript, store);
        coordinator.ApplySettings(EnabledAnalysisSettings());
        var active = CreateSegment();
        var queued = CreateSegment();
        Assert.IsTrue(coordinator.TryEnqueue(active));
        await analyzer.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsTrue(coordinator.TryEnqueue(queued));

        coordinator.ApplySettings(AudioContextSettings.Default);
        await WaitUntilAsync(() =>
            !coordinator.Diagnostic.RequestActive
            && active.MonoSamples.IsEmpty
            && queued.MonoSamples.IsEmpty);

        Assert.IsEmpty(transcript.List());
        Assert.IsTrue(active.MonoSamples.IsEmpty);
        Assert.IsTrue(queued.MonoSamples.IsEmpty);
    }

    [TestMethod]
    public async Task ClearObservationsClearsAudioRecordsTranscriptsAndQueuedPcm()
    {
        var analyzer = new BlockingAnalyzer();
        var transcript = new TranscriptWorkingBuffer();
        using (var existing = CreateSegment())
        {
            transcript.Add(existing, "temporary full transcript", 0.9);
        }

        var store = new AudioObservationStore(Path.Combine(_directory, "observations.json"));
        store.Add(CreateObservation("stored", DateTimeOffset.UtcNow));
        using var coordinator = new AudioAnalysisCoordinator(analyzer, transcript, store);
        coordinator.ApplySettings(EnabledAnalysisSettings());
        var active = CreateSegment();
        var queued = CreateSegment();
        Assert.IsTrue(coordinator.TryEnqueue(active));
        await analyzer.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsTrue(coordinator.TryEnqueue(queued));

        coordinator.ClearObservations();
        await WaitUntilAsync(() =>
            !coordinator.Diagnostic.RequestActive
            && active.MonoSamples.IsEmpty
            && queued.MonoSamples.IsEmpty);

        Assert.IsEmpty(store.List());
        Assert.IsEmpty(transcript.List());
        Assert.IsTrue(active.MonoSamples.IsEmpty);
        Assert.IsTrue(queued.MonoSamples.IsEmpty);
    }

    [TestMethod]
    public async Task ObservationTracksTemporaryTranscriptWhenExcerptStorageIsDisabled()
    {
        var analyzer = new SequenceAnalyzer(Success(transcript: "temporary only"));
        var store = new AudioObservationStore(Path.Combine(_directory, "observations.json"));
        using var coordinator = new AudioAnalysisCoordinator(
            analyzer,
            new TranscriptWorkingBuffer(),
            store);
        coordinator.ApplySettings(EnabledAnalysisSettings() with
        {
            PersistSystemAudioTranscriptExcerpt = false
        });

        Assert.IsTrue(coordinator.TryEnqueue(CreateSegment()));
        await WaitUntilAsync(() => !coordinator.Diagnostic.RequestActive);

        var observation = store.List().Single();
        Assert.IsNull(observation.TranscriptExcerpt);
        Assert.IsNotNull(observation.TranscriptExpiresAt);
    }

    [TestMethod]
    public void AgentContextDoesNotReadPersistedAudioObservations()
    {
        const string privateSummary = "audio-summary-that-must-not-enter-agent-context";
        var store = new AudioObservationStore(Path.Combine(_directory, "observations.json"));
        store.Add(CreateObservation("audio", DateTimeOffset.UtcNow) with
        {
            Summary = privateSummary,
            TranscriptExcerpt = "short excerpt"
        });

        var snapshot = AgentContextBuilder.Build(
            new ChatRequest(string.Empty),
            ChatHistoryContextSettings.Default);

        Assert.IsFalse(snapshot.Values.Keys.Any(key =>
            key.Contains("audio", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(snapshot.Values.Values.Any(value =>
            value.Contains(privateSummary, StringComparison.Ordinal)
            || value.Contains("short excerpt", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ExcerptsAreOneLineControlFreeAndCapped()
    {
        var source = new string('a', 170) + "\r\nnext";
        var excerpt = AudioAnalysisCoordinator.CreateExcerpt(source);

        Assert.IsNotNull(excerpt);
        Assert.HasCount(160, excerpt);
        Assert.IsFalse(excerpt.Any(char.IsControl));
    }

    private static AudioSegmentProcessingResult Process(
        AudioSegmentBuffer buffer,
        float amplitude,
        int milliseconds,
        ref DateTimeOffset now)
    {
        const int sampleRate = 1000;
        var samples = Enumerable.Repeat(amplitude, milliseconds).ToArray();
        now += TimeSpan.FromMilliseconds(milliseconds);
        return buffer.ProcessSamples(samples, sampleRate, now);
    }

    private static CompletedAudioSegment CreateSegment(
        AudioSourceKind source = AudioSourceKind.SystemAudio) =>
        new(
            Guid.NewGuid().ToString("N"),
            source,
            DateTimeOffset.UtcNow.AddSeconds(-1),
            DateTimeOffset.UtcNow,
            16000,
            Enumerable.Repeat(0.2f, 160).ToArray());

    private static AudioContextSettings EnabledAnalysisSettings() =>
        AudioContextSettings.Default with
        {
            Enabled = true,
            SystemAudioEnabled = true,
            AnalysisEnabled = true
        };

    private static AudioAnalysisResponse Success(
        string? transcript = "hello",
        double confidence = 0.9) =>
        new(
            "Fake",
            "fake/audio",
            AudioAnalysisStatus.Success,
            new AudioSemanticAnalysis(
                AudioDetectedKind.Speech,
                transcript,
                "Speech was detected.",
                ["speech"],
                confidence,
                AudioSensitivity.Normal,
                true,
                new AudioPolicySignals(1, 1, 0, false)),
            null);

    private static AudioAnalysisResponse Failure(AudioAnalysisStatus status) =>
        new("Fake", "fake/audio", status, null, new AudioAnalysisFailure("Safe failure."));

    private static AudioObservation CreateObservation(string id, DateTimeOffset createdAt) =>
        new(
            id,
            id,
            AudioSourceKind.SystemAudio,
            AudioDetectedKind.Music,
            createdAt.AddSeconds(-1),
            createdAt,
            "Music",
            ["music"],
            null,
            0.9,
            AudioSensitivity.Normal,
            "Fake",
            "fake/audio",
            AudioAnalysisStatus.Success,
            createdAt,
            null);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail("Timed out waiting for asynchronous audio analysis.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class BlockingAnalyzer : IAudioSegmentAnalyzer
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _concurrentCalls;

        public bool IsAvailable => true;
        public int CallCount { get; private set; }
        public int MaximumConcurrentCalls { get; private set; }
        public TaskCompletionSource FirstStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AudioAnalysisResponse> AnalyzeAsync(
            CompletedAudioSegment segment,
            AudioAnalysisOptions options,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var concurrent = Interlocked.Increment(ref _concurrentCalls);
            MaximumConcurrentCalls = Math.Max(MaximumConcurrentCalls, concurrent);
            FirstStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref _concurrentCalls);
            return Success();
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class SequenceAnalyzer(params AudioAnalysisResponse[] responses)
        : IAudioSegmentAnalyzer
    {
        private readonly Queue<AudioAnalysisResponse> _responses = new(responses);
        public bool IsAvailable => true;
        public int CallCount { get; private set; }

        public Task<AudioAnalysisResponse> AnalyzeAsync(
            CompletedAudioSegment segment,
            AudioAnalysisOptions options,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class FakeCaptureSource : IAudioCaptureSource
    {
        private DateTimeOffset _capturedAt = DateTimeOffset.UtcNow;

        public FakeCaptureSource(AudioSourceKind kind)
        {
            Kind = kind;
        }

        public AudioSourceKind Kind { get; }
        public string? DeviceName => $"{Kind} device";
        public string? FormatDescription => "1,000 Hz, 32-bit, 1 channel";
        public DateTimeOffset CapturedAt => _capturedAt;

        public event EventHandler<AudioSamplesAvailableEventArgs>? SamplesAvailable;
        public event EventHandler<Exception?>? CaptureFailed
        {
            add { }
            remove { }
        }

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }

        public void Emit(float amplitude, TimeSpan duration)
        {
            const int sampleRate = 1000;
            var samples = Enumerable.Repeat(
                amplitude,
                (int)(duration.TotalSeconds * sampleRate)).ToArray();
            _capturedAt += duration;
            SamplesAvailable?.Invoke(
                this,
                new AudioSamplesAvailableEventArgs(samples, sampleRate, _capturedAt));
        }
    }
}
