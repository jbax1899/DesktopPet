namespace DesktopPet.App.Audio;

public enum AudioSourceKind
{
    Microphone,
    SystemAudio
}

public enum AudioCaptureState
{
    Stopped,
    Starting,
    Capturing,
    Error
}

public enum AudioSegmentDisposition
{
    Completed,
    MaximumDuration,
    BelowActivityGate,
    BelowMinimumDuration
}

public sealed record AudioSegmentDiagnostic(
    AudioSourceKind Source,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    TimeSpan Duration,
    long ByteCount,
    double AverageRms,
    double Peak,
    AudioSegmentDisposition Disposition);

public sealed record AudioSourceDiagnostic(
    AudioSourceKind Source,
    AudioCaptureState State,
    string? DeviceName,
    string? Format,
    double CurrentLevel,
    TimeSpan ActiveSegmentDuration,
    int CompletedCount,
    int DiscardedCount,
    string? LastError,
    IReadOnlyList<AudioSegmentDiagnostic> RecentSegments);

public sealed class CompletedAudioSegment : IDisposable
{
    private float[] _samples;

    internal CompletedAudioSegment(
        string id,
        AudioSourceKind source,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        int sampleRate,
        float[] samples)
    {
        Id = id;
        Source = source;
        StartedAt = startedAt;
        EndedAt = endedAt;
        SampleRate = sampleRate;
        _samples = samples;
    }

    public string Id { get; }

    public AudioSourceKind Source { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset EndedAt { get; }

    public int SampleRate { get; }

    public ReadOnlyMemory<float> MonoSamples => _samples;

    public void Dispose()
    {
        Array.Clear(_samples);
        _samples = [];
    }
}

internal sealed record AudioSegmentProcessingResult(
    IReadOnlyList<AudioSegmentDiagnostic> Diagnostics,
    IReadOnlyList<CompletedAudioSegment> CompletedSegments)
{
    public static AudioSegmentProcessingResult Empty { get; } = new([], []);
}

public enum AudioDetectedKind
{
    Speech,
    Music,
    Silence,
    Noise,
    Mixed,
    Unknown
}

public enum AudioSensitivity
{
    Normal,
    PrivateConversation,
    Sensitive,
    Unknown
}

public enum AudioAnalysisStatus
{
    Success,
    Partial,
    Unavailable,
    TimedOut,
    Cancelled,
    ProviderRejected,
    InvalidResponse,
    Failed
}

public sealed record AudioPolicySignals(
    double Novelty,
    double Relevance,
    double InterruptionCost,
    bool ProviderSuggestedCommentary);

public sealed record AudioSemanticAnalysis(
    AudioDetectedKind DetectedKind,
    string? Transcript,
    string Summary,
    IReadOnlyList<string> EventLabels,
    double Confidence,
    AudioSensitivity Sensitivity,
    bool ShouldStore,
    AudioPolicySignals? PolicySignals);

public sealed record AudioAnalysisFailure(string SafeMessage);

public sealed record AudioAnalysisResponse(
    string Provider,
    string Model,
    AudioAnalysisStatus Status,
    AudioSemanticAnalysis? Analysis,
    AudioAnalysisFailure? Failure);

public sealed record AudioAnalysisOptions(
    bool RequestTranscript,
    int MaximumSummaryCharacters = 240,
    int MaximumEventLabels = 5);

public interface IAudioSegmentAnalyzer
{
    bool IsAvailable { get; }

    Task<AudioAnalysisResponse> AnalyzeAsync(
        CompletedAudioSegment segment,
        AudioAnalysisOptions options,
        CancellationToken cancellationToken);
}

public sealed record TranscriptWorkingChunk(
    string Id,
    string SegmentId,
    AudioSourceKind Source,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string Text,
    double Confidence,
    DateTimeOffset ExpiresAt);

public sealed record AudioObservation(
    string Id,
    string SegmentId,
    AudioSourceKind Source,
    AudioDetectedKind DetectedKind,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string Summary,
    IReadOnlyList<string> EventLabels,
    string? TranscriptExcerpt,
    double Confidence,
    AudioSensitivity Sensitivity,
    string Provider,
    string Model,
    AudioAnalysisStatus AnalysisStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? TranscriptExpiresAt);

public sealed record AudioAnalysisDiagnostic(
    bool Enabled,
    bool AnalyzerAvailable,
    bool RequestActive,
    int QueueDepth,
    int SuccessfulCount,
    int FailureCount,
    int DroppedCount,
    string? LastSafeFailure,
    DateTimeOffset? LastSuccessAt);

internal sealed class AudioSamplesAvailableEventArgs : EventArgs
{
    public AudioSamplesAvailableEventArgs(float[] monoSamples, int sampleRate, DateTimeOffset capturedAt)
    {
        MonoSamples = monoSamples;
        SampleRate = sampleRate;
        CapturedAt = capturedAt;
    }

    public float[] MonoSamples { get; }

    public int SampleRate { get; }

    public DateTimeOffset CapturedAt { get; }
}
