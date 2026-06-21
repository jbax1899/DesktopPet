using DesktopPet.App.Audio;

namespace DesktopPet.App.Tests;

[TestClass]
public sealed class AudioCaptureTests
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
    public void SettingsDefaultDisabledAndRoundTrip()
    {
        var path = Path.Combine(_directory, "audio-context-settings.json");
        var store = new AudioContextSettingsStore(path);

        Assert.AreEqual(AudioContextSettings.Default, store.Load());

        var saved = new AudioContextSettings(true, true, false);
        store.Save(saved);

        Assert.AreEqual(saved, store.Load());
    }

    [TestMethod]
    public void SettingsSupplyDefaultsForMissingFields()
    {
        var path = Path.Combine(_directory, "audio-context-settings.json");
        File.WriteAllText(path, """
        {
          "Enabled": true,
          "TranscriptRetentionMinutes": 5
        }
        """);

        var settings = new AudioContextSettingsStore(path).Load();

        Assert.IsTrue(settings.Enabled);
        Assert.IsFalse(settings.MicrophoneEnabled);
        Assert.IsFalse(settings.SystemAudioEnabled);
        Assert.IsFalse(settings.AnalysisEnabled);
        Assert.AreEqual(5, settings.ContextDepth);
        Assert.AreEqual(300, settings.TranscriptRetentionSeconds);
        Assert.AreEqual(100, settings.StoredObservationCount);
        Assert.AreEqual(0.60, settings.MinimumAnalysisConfidence);
    }

    [TestMethod]
    public void SilenceDoesNotCreateSegmentAndBufferStaysBounded()
    {
        var buffer = new AudioSegmentBuffer(AudioSourceKind.Microphone);
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 100; i++)
        {
            Feed(buffer, 0, TimeSpan.FromMilliseconds(100), ref now);
        }

        Assert.IsEmpty(buffer.RecentDiagnostics);
        Assert.IsFalse(buffer.HasActiveSegment);
        Assert.IsLessThanOrEqualTo(250, buffer.BufferedSampleCount);
    }

    [TestMethod]
    public void ShortSpikeIsDiscarded()
    {
        var buffer = new AudioSegmentBuffer(AudioSourceKind.Microphone);
        var now = DateTimeOffset.UtcNow;

        Feed(buffer, 0.2f, TimeSpan.FromMilliseconds(200), ref now);
        Feed(buffer, 0, TimeSpan.FromMilliseconds(100), ref now);

        var diagnostic = buffer.RecentDiagnostics.Single();
        Assert.AreEqual(AudioSegmentDisposition.BelowActivityGate, diagnostic.Disposition);
        Assert.IsFalse(buffer.HasActiveSegment);
    }

    [TestMethod]
    public void SustainedActivityClosesAfterTrailingSilenceAndReleasesSegmentBuffer()
    {
        var buffer = new AudioSegmentBuffer(AudioSourceKind.Microphone);
        var now = DateTimeOffset.UtcNow;

        Feed(buffer, 0.2f, TimeSpan.FromMilliseconds(500), ref now);
        Feed(buffer, 0.2f, TimeSpan.FromMilliseconds(600), ref now);
        Feed(buffer, 0, TimeSpan.FromMilliseconds(1500), ref now);

        var diagnostic = buffer.RecentDiagnostics.Single();
        Assert.AreEqual(AudioSegmentDisposition.Completed, diagnostic.Disposition);
        Assert.IsGreaterThan(0L, diagnostic.ByteCount);
        Assert.IsGreaterThan(0d, diagnostic.AverageRms);
        Assert.IsFalse(buffer.HasActiveSegment);
        Assert.IsLessThanOrEqualTo(250, buffer.BufferedSampleCount);
    }

    [TestMethod]
    public void ContinuousActivityForceClosesAtMaximumDuration()
    {
        var buffer = new AudioSegmentBuffer(AudioSourceKind.SystemAudio);
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 45; i++)
        {
            Feed(buffer, 0.2f, TimeSpan.FromMilliseconds(500), ref now);
        }

        Assert.IsTrue(buffer.RecentDiagnostics.Any(item =>
            item.Disposition == AudioSegmentDisposition.MaximumDuration));
        Assert.IsLessThanOrEqualTo(
            (int)(AudioSegmentBuffer.MaximumSegmentDuration.TotalSeconds * 1000)
                + (int)(AudioSegmentBuffer.PreRollDuration.TotalSeconds * 1000),
            buffer.BufferedSampleCount);
    }

    [TestMethod]
    public void DiagnosticsAreLimitedToTwentyPerSource()
    {
        var buffer = new AudioSegmentBuffer(AudioSourceKind.Microphone);
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 25; i++)
        {
            Feed(buffer, 0.2f, TimeSpan.FromMilliseconds(100), ref now);
            Feed(buffer, 0, TimeSpan.FromMilliseconds(100), ref now);
        }

        Assert.HasCount(AudioSegmentBuffer.MaximumDiagnostics, buffer.RecentDiagnostics);
    }

    [TestMethod]
    public void ReapplyingSettingsDoesNotRecreateActiveSources()
    {
        var created = new List<FakeCaptureSource>();
        using var coordinator = new AudioCaptureCoordinator(kind =>
        {
            var source = new FakeCaptureSource(kind);
            created.Add(source);
            return source;
        });
        var settings = new AudioContextSettings(true, true, true);

        coordinator.ApplySettings(settings);
        coordinator.ApplySettings(settings);

        Assert.HasCount(2, created);
        Assert.IsTrue(created.All(source => source.StartCount == 1));
        Assert.AreEqual(
            AudioCaptureState.Capturing,
            coordinator.GetDiagnostic(AudioSourceKind.Microphone).State);
        Assert.AreEqual(
            AudioCaptureState.Capturing,
            coordinator.GetDiagnostic(AudioSourceKind.SystemAudio).State);
    }

    [TestMethod]
    public void DisablingClearsActiveSegmentState()
    {
        FakeCaptureSource? microphone = null;
        using var coordinator = new AudioCaptureCoordinator(kind =>
        {
            var source = new FakeCaptureSource(kind);
            if (kind == AudioSourceKind.Microphone)
            {
                microphone = source;
            }

            return source;
        });

        coordinator.ApplySettings(new AudioContextSettings(true, true, false));
        microphone!.Emit(0.2f, TimeSpan.FromMilliseconds(600));
        Assert.IsGreaterThan(
            TimeSpan.Zero,
            coordinator.GetDiagnostic(AudioSourceKind.Microphone).ActiveSegmentDuration);

        coordinator.ApplySettings(AudioContextSettings.Default);

        var diagnostic = coordinator.GetDiagnostic(AudioSourceKind.Microphone);
        Assert.AreEqual(AudioCaptureState.Stopped, diagnostic.State);
        Assert.AreEqual(TimeSpan.Zero, diagnostic.ActiveSegmentDuration);
    }

    [TestMethod]
    public void OneSourceCanFailWhileOtherSourceCaptures()
    {
        using var coordinator = new AudioCaptureCoordinator(kind =>
            kind == AudioSourceKind.Microphone
                ? new FakeCaptureSource(kind, failOnStart: true)
                : new FakeCaptureSource(kind));

        coordinator.ApplySettings(new AudioContextSettings(true, true, true));

        Assert.AreEqual(
            AudioCaptureState.Error,
            coordinator.GetDiagnostic(AudioSourceKind.Microphone).State);
        Assert.AreEqual(
            AudioCaptureState.Capturing,
            coordinator.GetDiagnostic(AudioSourceKind.SystemAudio).State);
    }

    [TestMethod]
    public void MissingLoopbackCallbacksAreTreatedAsSilenceForActiveSegment()
    {
        FakeCaptureSource? systemAudio = null;
        using var coordinator = new AudioCaptureCoordinator(kind =>
        {
            var source = new FakeCaptureSource(kind);
            if (kind == AudioSourceKind.SystemAudio)
            {
                systemAudio = source;
            }

            return source;
        });

        coordinator.ApplySettings(new AudioContextSettings(true, false, true));
        systemAudio!.Emit(0.2f, TimeSpan.FromMilliseconds(1100));
        var end = systemAudio.CapturedAt + TimeSpan.FromSeconds(2);
        for (var now = systemAudio.CapturedAt + TimeSpan.FromMilliseconds(500);
             now <= end;
             now += TimeSpan.FromMilliseconds(500))
        {
            coordinator.ProcessSilenceGaps(now);
        }

        var diagnostic = coordinator.GetDiagnostic(AudioSourceKind.SystemAudio);
        Assert.AreEqual(1, diagnostic.CompletedCount);
        Assert.AreEqual(TimeSpan.Zero, diagnostic.ActiveSegmentDuration);
        Assert.AreEqual(0d, diagnostic.CurrentLevel);
    }

    [TestMethod]
    public void SpeechSuppressionDiscardsAudioAndResetsPartialSegments()
    {
        FakeCaptureSource? microphone = null;
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var coordinator = new AudioCaptureCoordinator(
            kind =>
            {
                var source = new FakeCaptureSource(kind);
                if (kind == AudioSourceKind.Microphone)
                {
                    microphone = source;
                }

                return source;
            },
            timeProvider: timeProvider);

        coordinator.ApplySettings(new AudioContextSettings(true, true, false));
        microphone!.Emit(0.2f, TimeSpan.FromMilliseconds(600));
        Assert.IsGreaterThan(
            TimeSpan.Zero,
            coordinator.GetDiagnostic(AudioSourceKind.Microphone).ActiveSegmentDuration);

        using (coordinator.SuppressForSpeech())
        {
            Assert.AreEqual(
                TimeSpan.Zero,
                coordinator.GetDiagnostic(AudioSourceKind.Microphone).ActiveSegmentDuration);

            microphone.Emit(0.2f, TimeSpan.FromSeconds(2));
            Assert.AreEqual(
                TimeSpan.Zero,
                coordinator.GetDiagnostic(AudioSourceKind.Microphone).ActiveSegmentDuration);
        }

        microphone.Emit(0.2f, TimeSpan.FromMilliseconds(600));
        Assert.AreEqual(
            TimeSpan.Zero,
            coordinator.GetDiagnostic(AudioSourceKind.Microphone).ActiveSegmentDuration);

        timeProvider.Advance(AudioCaptureCoordinator.SpeechSuppressionCooldown);
        microphone.Emit(0.2f, TimeSpan.FromMilliseconds(600));
        Assert.IsGreaterThan(
            TimeSpan.Zero,
            coordinator.GetDiagnostic(AudioSourceKind.Microphone).ActiveSegmentDuration);
    }

    private static void Feed(
        AudioSegmentBuffer buffer,
        float amplitude,
        TimeSpan duration,
        ref DateTimeOffset now)
    {
        const int sampleRate = 1000;
        var samples = Enumerable.Repeat(amplitude, (int)(duration.TotalSeconds * sampleRate)).ToArray();
        now += duration;
        var result = buffer.ProcessSamples(samples, sampleRate, now);
        foreach (var segment in result.CompletedSegments)
        {
            segment.Dispose();
        }
    }

    private sealed class FakeCaptureSource : IAudioCaptureSource
    {
        private readonly bool _failOnStart;
        private DateTimeOffset _capturedAt = DateTimeOffset.UtcNow;

        public FakeCaptureSource(AudioSourceKind kind, bool failOnStart = false)
        {
            Kind = kind;
            _failOnStart = failOnStart;
        }

        public AudioSourceKind Kind { get; }

        public string? DeviceName => $"{Kind} device";

        public string? FormatDescription => "1,000 Hz, 32-bit, 1 channel";

        public int StartCount { get; private set; }

        public DateTimeOffset CapturedAt => _capturedAt;

        public event EventHandler<AudioSamplesAvailableEventArgs>? SamplesAvailable;

        public event EventHandler<Exception?>? CaptureFailed;

        public void Start()
        {
            StartCount++;
            if (_failOnStart)
            {
                throw new InvalidOperationException("Test capture failure.");
            }
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
            var samples = Enumerable.Repeat(amplitude, (int)(duration.TotalSeconds * sampleRate)).ToArray();
            _capturedAt += duration;
            SamplesAvailable?.Invoke(
                this,
                new AudioSamplesAvailableEventArgs(samples, sampleRate, _capturedAt));
        }

        public void Fail(Exception exception)
        {
            CaptureFailed?.Invoke(this, exception);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
        }
    }
}
