namespace DesktopPet.App.Audio;

public sealed class AudioCaptureCoordinator : IDisposable
{
    private readonly object _sync = new();
    private readonly Func<AudioSourceKind, IAudioCaptureSource> _sourceFactory;
    private readonly AudioAnalysisCoordinator? _analysisCoordinator;
    private readonly Dictionary<AudioSourceKind, SourceSession> _sessions;
    private readonly System.Threading.Timer _silenceTimer;
    private bool _disposed;

    public AudioCaptureCoordinator()
        : this(kind => kind switch
        {
            AudioSourceKind.Microphone => new MicrophoneCaptureSource(),
            AudioSourceKind.SystemAudio => new SystemLoopbackCaptureSource(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        }, null)
    {
    }

    internal AudioCaptureCoordinator(
        Func<AudioSourceKind, IAudioCaptureSource> sourceFactory,
        AudioAnalysisCoordinator? analysisCoordinator = null)
    {
        _sourceFactory = sourceFactory;
        _analysisCoordinator = analysisCoordinator;
        _sessions = Enum.GetValues<AudioSourceKind>()
            .ToDictionary(kind => kind, kind => new SourceSession(kind));
        _silenceTimer = new System.Threading.Timer(
            _ => ProcessSilenceGaps(DateTimeOffset.UtcNow),
            null,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(250));
    }

    public void ApplySettings(AudioContextSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_sync)
        {
            ApplySource(
                _sessions[AudioSourceKind.Microphone],
                settings.Enabled && settings.MicrophoneEnabled);
            ApplySource(
                _sessions[AudioSourceKind.SystemAudio],
                settings.Enabled && settings.SystemAudioEnabled);
        }

        _analysisCoordinator?.ApplySettings(settings);
    }

    public AudioSourceDiagnostic GetDiagnostic(AudioSourceKind source)
    {
        lock (_sync)
        {
            var session = _sessions[source];
            var recent = session.SegmentBuffer.RecentDiagnostics;
            return new AudioSourceDiagnostic(
                source,
                session.State,
                session.DeviceName,
                session.Format,
                session.CurrentLevel,
                session.SegmentBuffer.ActiveSegmentDuration,
                session.CompletedCount,
                session.DiscardedCount,
                session.LastError,
                recent);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            foreach (var session in _sessions.Values)
            {
                StopSource(session, clearDiagnostics: true);
            }

            _disposed = true;
        }

        _silenceTimer.Dispose();
    }

    private void ApplySource(SourceSession session, bool enabled)
    {
        if (!enabled)
        {
            StopSource(session, clearDiagnostics: false);
            return;
        }

        if (session.Source is not null && session.State == AudioCaptureState.Capturing)
        {
            return;
        }

        StopSource(session, clearDiagnostics: false);
        session.State = AudioCaptureState.Starting;
        session.LastError = null;

        IAudioCaptureSource? source = null;
        try
        {
            source = _sourceFactory(session.Kind);
            source.SamplesAvailable += OnSamplesAvailable;
            source.CaptureFailed += OnCaptureFailed;
            session.Source = source;
            source.Start();
            session.DeviceName = source.DeviceName;
            session.Format = source.FormatDescription;
            session.State = AudioCaptureState.Capturing;
        }
        catch (Exception ex)
        {
            if (source is not null)
            {
                source.SamplesAvailable -= OnSamplesAvailable;
                source.CaptureFailed -= OnCaptureFailed;
                source.Dispose();
            }

            session.Source = null;
            session.State = AudioCaptureState.Error;
            session.LastError = ex.Message;
        }
    }

    private void StopSource(SourceSession session, bool clearDiagnostics)
    {
        var source = session.Source;
        session.Source = null;
        if (source is not null)
        {
            source.SamplesAvailable -= OnSamplesAvailable;
            source.CaptureFailed -= OnCaptureFailed;
            source.Stop();
            source.Dispose();
        }

        session.State = AudioCaptureState.Stopped;
        session.CurrentLevel = 0;
            session.LastError = null;
            session.LastFrameAt = null;
            session.LastSilenceAt = null;
            session.SampleRate = 0;
            if (clearDiagnostics)
            {
                session.CompletedCount = 0;
                session.DiscardedCount = 0;
            }
            session.SegmentBuffer.Reset(clearDiagnostics);
    }

    private void OnSamplesAvailable(object? sender, AudioSamplesAvailableEventArgs e)
    {
        if (sender is not IAudioCaptureSource source)
        {
            return;
        }

        lock (_sync)
        {
            var session = _sessions[source.Kind];
            if (!ReferenceEquals(session.Source, source)
                || session.State != AudioCaptureState.Capturing)
            {
                return;
            }

            session.CurrentLevel = CalculateRms(e.MonoSamples);
            session.SampleRate = e.SampleRate;
            session.LastFrameAt = e.CapturedAt;
            session.LastSilenceAt = null;
            RecordResult(
                session,
                session.SegmentBuffer.ProcessSamples(e.MonoSamples, e.SampleRate, e.CapturedAt));
        }
    }

    private void OnCaptureFailed(object? sender, Exception? exception)
    {
        if (sender is not IAudioCaptureSource source)
        {
            return;
        }

        lock (_sync)
        {
            var session = _sessions[source.Kind];
            if (!ReferenceEquals(session.Source, source))
            {
                return;
            }

            source.SamplesAvailable -= OnSamplesAvailable;
            source.CaptureFailed -= OnCaptureFailed;
            session.Source = null;
            session.State = AudioCaptureState.Error;
            session.CurrentLevel = 0;
            session.LastError = exception?.Message ?? "Audio capture failed.";
            session.LastFrameAt = null;
            session.LastSilenceAt = null;
            session.SampleRate = 0;
            session.SegmentBuffer.Reset(clearDiagnostics: false);
            _ = Task.Run(source.Dispose);
        }
    }

    internal void ProcessSilenceGaps(DateTimeOffset now)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var session in _sessions.Values)
            {
                if (session.State != AudioCaptureState.Capturing
                    || session.SampleRate <= 0
                    || session.LastFrameAt is null
                    || now - session.LastFrameAt.Value < TimeSpan.FromMilliseconds(250))
                {
                    continue;
                }

                session.CurrentLevel = 0;
                if (!session.SegmentBuffer.HasBufferedActivity)
                {
                    continue;
                }

                var silenceStart = session.LastSilenceAt ?? session.LastFrameAt.Value;
                var elapsed = now - silenceStart;
                if (elapsed <= TimeSpan.Zero)
                {
                    continue;
                }

                var injectedDuration = elapsed > TimeSpan.FromMilliseconds(500)
                    ? TimeSpan.FromMilliseconds(500)
                    : elapsed;
                var sampleCount = Math.Max(
                    1,
                    (int)Math.Ceiling(injectedDuration.TotalSeconds * session.SampleRate));
                RecordResult(
                    session,
                    session.SegmentBuffer.ProcessSamples(
                        new float[sampleCount],
                        session.SampleRate,
                        silenceStart + injectedDuration));
                session.LastSilenceAt = silenceStart + injectedDuration;
            }
        }
    }

    private static bool IsCompleted(AudioSegmentDiagnostic diagnostic) =>
        diagnostic.Disposition is AudioSegmentDisposition.Completed
            or AudioSegmentDisposition.MaximumDuration;

    private void RecordResult(
        SourceSession session,
        AudioSegmentProcessingResult result)
    {
        foreach (var diagnostic in result.Diagnostics)
        {
            if (IsCompleted(diagnostic))
            {
                session.CompletedCount++;
            }
            else
            {
                session.DiscardedCount++;
            }
        }

        foreach (var segment in result.CompletedSegments)
        {
            if (_analysisCoordinator is null || !_analysisCoordinator.TryEnqueue(segment))
            {
                segment.Dispose();
            }
        }
    }

    private static double CalculateRms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return 0;
        }

        var sumSquares = 0d;
        foreach (var sample in samples)
        {
            sumSquares += sample * sample;
        }

        return Math.Sqrt(sumSquares / samples.Length);
    }

    private sealed class SourceSession
    {
        public SourceSession(AudioSourceKind kind)
        {
            Kind = kind;
            SegmentBuffer = new AudioSegmentBuffer(kind);
        }

        public AudioSourceKind Kind { get; }

        public IAudioCaptureSource? Source { get; set; }

        public AudioSegmentBuffer SegmentBuffer { get; }

        public AudioCaptureState State { get; set; }

        public string? DeviceName { get; set; }

        public string? Format { get; set; }

        public double CurrentLevel { get; set; }

        public string? LastError { get; set; }

        public int SampleRate { get; set; }

        public DateTimeOffset? LastFrameAt { get; set; }

        public DateTimeOffset? LastSilenceAt { get; set; }

        public int CompletedCount { get; set; }

        public int DiscardedCount { get; set; }
    }
}
