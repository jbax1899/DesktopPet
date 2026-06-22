namespace DesktopPet.App.Audio;

public sealed class AudioCaptureCoordinator : IDisposable
{
    internal static readonly TimeSpan SpeechSuppressionCooldown = TimeSpan.FromMilliseconds(500);

    private readonly object _sync = new();
    private readonly Func<AudioSourceKind, string?, IAudioCaptureSource> _sourceFactory;
    private readonly AudioAnalysisCoordinator? _analysisCoordinator;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<AudioSourceKind, SourceSession> _sessions;
    private readonly Dictionary<string, SourceSession> _perAppSessions;
    private readonly System.Threading.Timer _silenceTimer;
    private string? _microphoneDeviceId;
    private string? _systemAudioDeviceId;
    private int _speechSuppressionCount;
    private DateTimeOffset _speechSuppressedUntil;
    private bool _disposed;

    private IAudioCaptureSource? _pushToTalkSource;
    private readonly List<float> _pushToTalkBuffer = [];
    private DateTimeOffset _pushToTalkStartedAt;
    private int _pushToTalkSampleRate;
    private bool _isPushToTalkRecording;

    public AudioCaptureCoordinator()
        : this((kind, deviceId) => kind switch
        {
            AudioSourceKind.Microphone => new MicrophoneCaptureSource(deviceId),
            AudioSourceKind.SystemAudio => new SystemLoopbackCaptureSource(deviceId),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        }, null, TimeProvider.System)
    {
    }

    internal AudioCaptureCoordinator(
        Func<AudioSourceKind, string?, IAudioCaptureSource> sourceFactory,
        AudioAnalysisCoordinator? analysisCoordinator = null,
        TimeProvider? timeProvider = null)
    {
        _sourceFactory = sourceFactory;
        _analysisCoordinator = analysisCoordinator;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _sessions = Enum.GetValues<AudioSourceKind>()
            .ToDictionary(kind => kind, kind => new SourceSession(kind));
        _perAppSessions = new Dictionary<string, SourceSession>(StringComparer.OrdinalIgnoreCase);
        _silenceTimer = new System.Threading.Timer(
            _ => ProcessSilenceGaps(_timeProvider.GetUtcNow()),
            null,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(250));
    }

    public void ApplySettings(AudioContextSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var maxDuration = TimeSpan.FromSeconds(settings.MaximumSegmentDurationSeconds);
        lock (_sync)
        {
            _microphoneDeviceId = settings.MicrophoneDeviceId;
            _systemAudioDeviceId = settings.SystemAudioDeviceId;
            ApplySource(
                _sessions[AudioSourceKind.Microphone],
                settings.Enabled && settings.MicrophoneEnabled,
                maxDuration);
            ApplySource(
                _sessions[AudioSourceKind.SystemAudio],
                settings.Enabled && settings.SystemAudioEnabled,
                maxDuration);
        }

        _analysisCoordinator?.ApplySettings(settings);
    }

    // TODO: Simplify when NAudio handles process loopback natively (PR #1225).
    public void ApplyPerAppCaptures(
        IReadOnlyList<AudioApplicationRule> rules,
        bool systemAudioEnabled,
        TimeSpan maxDuration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_sync)
        {
            if (systemAudioEnabled)
            {
                foreach (var session in _perAppSessions.Values)
                {
                    StopSource(session, clearDiagnostics: false);
                }

                _perAppSessions.Clear();
                return;
            }

            var desiredPaths = new HashSet<string>(
                rules.Where(r => r.AllowCapture).Select(r => r.ExecutablePath),
                StringComparer.OrdinalIgnoreCase);

            var toRemove = _perAppSessions.Keys
                .Where(k => !desiredPaths.Contains(k))
                .ToList();
            foreach (var path in toRemove)
            {
                StopSource(_perAppSessions[path], clearDiagnostics: false);
                _perAppSessions.Remove(path);
            }

            foreach (var rule in rules.Where(r => r.AllowCapture))
            {
                if (_perAppSessions.TryGetValue(rule.ExecutablePath, out var existing))
                {
                    if (existing.Source is not null && existing.State == AudioCaptureState.Capturing)
                    {
                        continue;
                    }

                    StopSource(existing, clearDiagnostics: false);
                }

                var session = new SourceSession(AudioSourceKind.ProcessAudio);
                session.SegmentBuffer.UpdateMaximumDuration(maxDuration);
                _perAppSessions[rule.ExecutablePath] = session;

                TryStartPerAppSource(session, rule.ExecutablePath);
            }
        }
    }

    public IDisposable SuppressForSpeech()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _speechSuppressionCount++;
            ResetBufferedAudio();
        }

        return new SpeechSuppressionScope(this);
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
            StopPushToTalkSource();
            foreach (var session in _sessions.Values)
            {
                StopSource(session, clearDiagnostics: true);
            }

            foreach (var session in _perAppSessions.Values)
            {
                StopSource(session, clearDiagnostics: true);
            }

            _perAppSessions.Clear();
            _speechSuppressionCount = 0;
            _speechSuppressedUntil = DateTimeOffset.MinValue;
            _disposed = true;
        }

        _silenceTimer.Dispose();
    }

    public bool IsPushToTalkRecording
    {
        get
        {
            lock (_sync)
            {
                return _isPushToTalkRecording;
            }
        }
    }

    public void StartPushToTalkRecording()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_isPushToTalkRecording)
            {
                return;
            }

            var micSession = _sessions[AudioSourceKind.Microphone];
            if (micSession.State == AudioCaptureState.Capturing)
            {
                StopSource(micSession, clearDiagnostics: false);
            }

            _pushToTalkBuffer.Clear();
            _pushToTalkSampleRate = 0;
            _pushToTalkStartedAt = _timeProvider.GetUtcNow();
            _isPushToTalkRecording = true;

            IAudioCaptureSource? source = null;
            try
            {
                source = _sourceFactory(AudioSourceKind.Microphone, _microphoneDeviceId);
                source.SamplesAvailable += OnPushToTalkSamplesAvailable;
                source.CaptureFailed += OnPushToTalkCaptureFailed;
                _pushToTalkSource = source;
                source.Start();
                _pushToTalkSampleRate = 0;
            }
            catch (Exception)
            {
                if (source is not null)
                {
                    source.SamplesAvailable -= OnPushToTalkSamplesAvailable;
                    source.CaptureFailed -= OnPushToTalkCaptureFailed;
                    source.Dispose();
                }

                _pushToTalkSource = null;
                _isPushToTalkRecording = false;
                throw;
            }
        }
    }

    public CompletedAudioSegment? StopPushToTalkRecording()
    {
        lock (_sync)
        {
            if (!_isPushToTalkRecording)
            {
                return null;
            }

            StopPushToTalkSource();
            _isPushToTalkRecording = false;

            var now = _timeProvider.GetUtcNow();
            var duration = now - _pushToTalkStartedAt;
            if (duration < TimeSpan.FromMilliseconds(500) || _pushToTalkBuffer.Count == 0 || _pushToTalkSampleRate <= 0)
            {
                _pushToTalkBuffer.Clear();
                return null;
            }

            var segment = new CompletedAudioSegment(
                Guid.NewGuid().ToString("N"),
                AudioSourceKind.Microphone,
                _pushToTalkStartedAt,
                now,
                _pushToTalkSampleRate,
                _pushToTalkBuffer.ToArray());
            _pushToTalkBuffer.Clear();
            return segment;
        }
    }

    private void OnPushToTalkSamplesAvailable(object? sender, AudioSamplesAvailableEventArgs e)
    {
        if (sender is not IAudioCaptureSource source)
        {
            return;
        }

        lock (_sync)
        {
            if (!ReferenceEquals(_pushToTalkSource, source) || !_isPushToTalkRecording)
            {
                return;
            }

            if (_pushToTalkSampleRate <= 0)
            {
                _pushToTalkSampleRate = e.SampleRate;
            }

            _pushToTalkBuffer.AddRange(e.MonoSamples);
        }
    }

    private void OnPushToTalkCaptureFailed(object? sender, Exception? exception)
    {
        if (sender is not IAudioCaptureSource source)
        {
            return;
        }

        lock (_sync)
        {
            if (!ReferenceEquals(_pushToTalkSource, source))
            {
                return;
            }

            source.SamplesAvailable -= OnPushToTalkSamplesAvailable;
            source.CaptureFailed -= OnPushToTalkCaptureFailed;
            _pushToTalkSource = null;
            _isPushToTalkRecording = false;
            _pushToTalkBuffer.Clear();
            _ = Task.Run(source.Dispose);
        }
    }

    private void StopPushToTalkSource()
    {
        var source = _pushToTalkSource;
        _pushToTalkSource = null;
        if (source is not null)
        {
            source.SamplesAvailable -= OnPushToTalkSamplesAvailable;
            source.CaptureFailed -= OnPushToTalkCaptureFailed;
            source.Stop();
            source.Dispose();
        }
    }

    private void ApplySource(SourceSession session, bool enabled, TimeSpan maxDuration)
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
        session.SegmentBuffer.UpdateMaximumDuration(maxDuration);
        session.State = AudioCaptureState.Starting;
        session.LastError = null;

        var deviceId = session.Kind switch
        {
            AudioSourceKind.Microphone => _microphoneDeviceId,
            AudioSourceKind.SystemAudio => _systemAudioDeviceId,
            _ => null
        };

        IAudioCaptureSource? source = null;
        try
        {
            source = _sourceFactory(session.Kind, deviceId);
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

    // TODO: Simplify when NAudio handles process loopback natively (PR #1225).
    private bool TryStartPerAppSource(SourceSession session, string executablePath)
    {
        session.State = AudioCaptureState.Starting;
        session.LastError = null;

        var processId = FindProcessIdForPath(executablePath);
        if (processId is null)
        {
            session.State = AudioCaptureState.Error;
            session.LastError = "Process not found or not running.";
            return false;
        }

        IAudioCaptureSource? source = null;
        try
        {
            source = new ProcessLoopbackCaptureSource(processId.Value);
            source.SamplesAvailable += OnSamplesAvailable;
            source.CaptureFailed += OnCaptureFailed;
            session.Source = source;
            source.Start();
            session.DeviceName = source.DeviceName;
            session.Format = source.FormatDescription;
            session.State = AudioCaptureState.Capturing;
            return true;
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
            return false;
        }
    }

    private static int? FindProcessIdForPath(string executablePath)
    {
        try
        {
            var normalizedPath = System.IO.Path.GetFullPath(executablePath);
            foreach (var process in System.Diagnostics.Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.MainModule?.FileName is string path
                            && string.Equals(path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            return process.Id;
                        }
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private SourceSession? FindSessionBySource(IAudioCaptureSource source)
    {
        if (_sessions.TryGetValue(source.Kind, out var fixedSession)
            && ReferenceEquals(fixedSession.Source, source))
        {
            return fixedSession;
        }

        foreach (var session in _perAppSessions.Values)
        {
            if (ReferenceEquals(session.Source, source))
            {
                return session;
            }
        }

        return null;
    }

    private void StopSource(SourceSession session, bool clearDiagnostics)
    {
        var source = session.Source;
        session.Source = null;
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

        if (source is not null)
        {
            source.SamplesAvailable -= OnSamplesAvailable;
            source.CaptureFailed -= OnCaptureFailed;
            try
            {
                source.Stop();
            }
            catch
            {
            }

            _ = Task.Run(source.Dispose);
        }
    }

    private void OnSamplesAvailable(object? sender, AudioSamplesAvailableEventArgs e)
    {
        if (sender is not IAudioCaptureSource source)
        {
            return;
        }

        lock (_sync)
        {
            var session = FindSessionBySource(source);
            if (session is null
                || session.State != AudioCaptureState.Capturing)
            {
                return;
            }

            if (IsSpeechSuppressed(_timeProvider.GetUtcNow()))
            {
                session.CurrentLevel = 0;
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
            var session = FindSessionBySource(source);
            if (session is null)
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

            foreach (var session in _sessions.Values.Concat(_perAppSessions.Values))
            {
                if (session.State != AudioCaptureState.Capturing
                    || IsSpeechSuppressed(now)
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

    private bool IsSpeechSuppressed(DateTimeOffset now) =>
        _speechSuppressionCount > 0 || now < _speechSuppressedUntil;

    private void EndSpeechSuppression()
    {
        lock (_sync)
        {
            if (_disposed || _speechSuppressionCount == 0)
            {
                return;
            }

            _speechSuppressionCount--;
            if (_speechSuppressionCount > 0)
            {
                return;
            }

            ResetBufferedAudio();
            _speechSuppressedUntil = _timeProvider.GetUtcNow() + SpeechSuppressionCooldown;
        }
    }

    private void ResetBufferedAudio()
    {
        foreach (var session in _sessions.Values.Concat(_perAppSessions.Values))
        {
            session.CurrentLevel = 0;
            session.LastFrameAt = null;
            session.LastSilenceAt = null;
            session.SampleRate = 0;
            session.SegmentBuffer.Reset(clearDiagnostics: false);
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
        public SourceSession(AudioSourceKind kind, TimeSpan? maximumSegmentDuration = null)
        {
            Kind = kind;
            SegmentBuffer = new AudioSegmentBuffer(kind, maximumSegmentDuration);
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

    private sealed class SpeechSuppressionScope : IDisposable
    {
        private AudioCaptureCoordinator? _owner;

        public SpeechSuppressionScope(AudioCaptureCoordinator owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.EndSpeechSuppression();
        }
    }
}
