namespace DesktopPet.App.Audio;

internal sealed class AudioSegmentBuffer
{
    internal const double ActivityThreshold = 0.02;
    internal static readonly TimeSpan ActivityGate = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan PreRollDuration = TimeSpan.FromMilliseconds(250);
    internal static readonly TimeSpan TrailingSilenceDuration = TimeSpan.FromMilliseconds(1500);
    internal static readonly TimeSpan MinimumActiveDuration = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan MaximumSegmentDuration = TimeSpan.FromSeconds(20);
    internal const int MaximumDiagnostics = 20;

    private readonly AudioSourceKind _source;
    private readonly Queue<float> _preRoll = new();
    private readonly List<float> _pending = [];
    private readonly List<float> _segment = [];
    private readonly Queue<AudioSegmentDiagnostic> _recent = new();

    private int _sampleRate;
    private long _pendingActiveSamples;
    private DateTimeOffset _pendingStartedAt;
    private long _activeSamples;
    private long _trailingSilenceSamples;
    private DateTimeOffset _segmentStartedAt;
    private double _sumSquares;
    private double _peak;

    public AudioSegmentBuffer(AudioSourceKind source)
    {
        _source = source;
    }

    public bool HasActiveSegment => _segment.Count > 0;

    public bool HasBufferedActivity => _pending.Count > 0 || _segment.Count > 0;

    public int BufferedSampleCount => _preRoll.Count + _pending.Count + _segment.Count;

    public TimeSpan ActiveSegmentDuration =>
        _sampleRate <= 0 || _segment.Count == 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds((double)_segment.Count / _sampleRate);

    public IReadOnlyList<AudioSegmentDiagnostic> RecentDiagnostics => _recent.ToArray();

    public AudioSegmentProcessingResult ProcessSamples(
        ReadOnlySpan<float> samples,
        int sampleRate,
        DateTimeOffset capturedAt)
    {
        if (samples.IsEmpty || sampleRate <= 0)
        {
            return AudioSegmentProcessingResult.Empty;
        }

        if (_sampleRate != 0 && _sampleRate != sampleRate)
        {
            Reset(clearDiagnostics: false);
        }

        _sampleRate = sampleRate;
        var frameRms = CalculateRms(samples);
        var active = frameRms >= ActivityThreshold;
        AudioSegmentDiagnostic? diagnostic = null;
        CompletedAudioSegment? completedSegment = null;

        if (_segment.Count > 0)
        {
            AppendSegment(samples, active);
            if (_segment.Count >= SamplesFor(MaximumSegmentDuration))
            {
                (diagnostic, completedSegment) = CloseSegment(
                    capturedAt,
                    AudioSegmentDisposition.MaximumDuration);
            }
            else if (_trailingSilenceSamples >= SamplesFor(TrailingSilenceDuration))
            {
                (diagnostic, completedSegment) = CloseSegment(
                    capturedAt,
                    _activeSamples >= SamplesFor(MinimumActiveDuration)
                        ? AudioSegmentDisposition.Completed
                        : AudioSegmentDisposition.BelowMinimumDuration);
            }
        }
        else if (active)
        {
            if (_pending.Count == 0)
            {
                _pendingStartedAt = capturedAt
                    - TimeSpan.FromSeconds((double)samples.Length / sampleRate)
                    - TimeSpan.FromSeconds((double)_preRoll.Count / sampleRate);
                _pending.AddRange(_preRoll);
            }

            Append(_pending, samples);
            _pendingActiveSamples += samples.Length;
            if (_pendingActiveSamples >= SamplesFor(ActivityGate))
            {
                OpenSegment();
            }
        }
        else if (_pending.Count > 0)
        {
            diagnostic = DiscardPending(capturedAt);
        }

        if (_segment.Count == 0)
        {
            AddPreRoll(samples);
        }

        return new AudioSegmentProcessingResult(
            diagnostic is null ? [] : [diagnostic],
            completedSegment is null ? [] : [completedSegment]);
    }

    public void Reset(bool clearDiagnostics)
    {
        _preRoll.Clear();
        _pending.Clear();
        _segment.Clear();
        _sampleRate = 0;
        _pendingActiveSamples = 0;
        _activeSamples = 0;
        _trailingSilenceSamples = 0;
        _sumSquares = 0;
        _peak = 0;
        if (clearDiagnostics)
        {
            _recent.Clear();
        }
    }

    private void OpenSegment()
    {
        _segmentStartedAt = _pendingStartedAt;
        _segment.AddRange(_pending);
        _activeSamples = _pendingActiveSamples;
        Accumulate(_pending);
        _pending.Clear();
        _pendingActiveSamples = 0;
        _preRoll.Clear();
    }

    private void AppendSegment(ReadOnlySpan<float> samples, bool active)
    {
        foreach (var sample in samples)
        {
            _segment.Add(sample);
        }

        Accumulate(samples);
        if (active)
        {
            _activeSamples += samples.Length;
            _trailingSilenceSamples = 0;
        }
        else
        {
            _trailingSilenceSamples += samples.Length;
        }
    }

    private (AudioSegmentDiagnostic Diagnostic, CompletedAudioSegment? Segment) CloseSegment(
        DateTimeOffset capturedAt,
        AudioSegmentDisposition disposition)
    {
        var sampleCount = _segment.Count;
        var diagnostic = AddDiagnostic(
            _segmentStartedAt,
            capturedAt,
            sampleCount,
            _sumSquares,
            _peak,
            disposition);

        CompletedAudioSegment? segment = null;
        if (disposition is AudioSegmentDisposition.Completed or AudioSegmentDisposition.MaximumDuration)
        {
            segment = new CompletedAudioSegment(
                Guid.NewGuid().ToString("N"),
                _source,
                _segmentStartedAt,
                capturedAt,
                _sampleRate,
                _segment.ToArray());
        }

        _segment.Clear();
        _activeSamples = 0;
        _trailingSilenceSamples = 0;
        _sumSquares = 0;
        _peak = 0;
        return (diagnostic, segment);
    }

    private AudioSegmentDiagnostic DiscardPending(DateTimeOffset capturedAt)
    {
        var sumSquares = 0d;
        var peak = 0d;
        foreach (var sample in _pending)
        {
            sumSquares += sample * sample;
            peak = Math.Max(peak, Math.Abs(sample));
        }

        var diagnostic = AddDiagnostic(
            _pendingStartedAt,
            capturedAt,
            _pending.Count,
            sumSquares,
            peak,
            AudioSegmentDisposition.BelowActivityGate);
        _pending.Clear();
        _pendingActiveSamples = 0;
        return diagnostic;
    }

    private AudioSegmentDiagnostic AddDiagnostic(
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        int sampleCount,
        double sumSquares,
        double peak,
        AudioSegmentDisposition disposition)
    {
        var diagnostic = new AudioSegmentDiagnostic(
            _source,
            startedAt,
            endedAt,
            endedAt - startedAt,
            sampleCount * sizeof(float),
            sampleCount == 0 ? 0 : Math.Sqrt(sumSquares / sampleCount),
            peak,
            disposition);
        _recent.Enqueue(diagnostic);
        while (_recent.Count > MaximumDiagnostics)
        {
            _recent.Dequeue();
        }

        return diagnostic;
    }

    private void AddPreRoll(ReadOnlySpan<float> samples)
    {
        var maximumSamples = SamplesFor(PreRollDuration);
        foreach (var sample in samples)
        {
            _preRoll.Enqueue(sample);
        }

        while (_preRoll.Count > maximumSamples)
        {
            _preRoll.Dequeue();
        }
    }

    private void Accumulate(IEnumerable<float> samples)
    {
        foreach (var sample in samples)
        {
            _sumSquares += sample * sample;
            _peak = Math.Max(_peak, Math.Abs(sample));
        }
    }

    private void Accumulate(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            _sumSquares += sample * sample;
            _peak = Math.Max(_peak, Math.Abs(sample));
        }
    }

    private long SamplesFor(TimeSpan duration) =>
        Math.Max(1, (long)Math.Ceiling(duration.TotalSeconds * _sampleRate));

    private static double CalculateRms(ReadOnlySpan<float> samples)
    {
        var sumSquares = 0d;
        foreach (var sample in samples)
        {
            sumSquares += sample * sample;
        }

        return Math.Sqrt(sumSquares / samples.Length);
    }

    private static void Append(ICollection<float> destination, ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            destination.Add(sample);
        }
    }
}
