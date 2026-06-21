namespace DesktopPet.App.Audio;

public enum AudioTranscriptDetail
{
    Brief,
    Detailed,
    Transcript
}

public sealed record AudioContextSettings(
    bool Enabled,
    bool MicrophoneEnabled,
    bool SystemAudioEnabled,
    bool AnalysisEnabled,
    bool PersistMicrophoneTranscriptExcerpt,
    bool PersistSystemAudioTranscriptExcerpt,
    AudioTranscriptDetail TranscriptDetail,
    int ContextDepth,
    int TranscriptRetentionSeconds,
    int StoredObservationCount,
    double MinimumAnalysisConfidence,
    int AnalysisTimeoutSeconds)
{
    public AudioContextSettings(bool enabled, bool microphoneEnabled, bool systemAudioEnabled)
        : this(
            enabled,
            microphoneEnabled,
            systemAudioEnabled,
            Default.AnalysisEnabled,
            Default.PersistMicrophoneTranscriptExcerpt,
            Default.PersistSystemAudioTranscriptExcerpt,
            Default.TranscriptDetail,
            Default.ContextDepth,
            Default.TranscriptRetentionSeconds,
            Default.StoredObservationCount,
            Default.MinimumAnalysisConfidence,
            Default.AnalysisTimeoutSeconds)
    {
    }

    public static AudioContextSettings Default { get; } = new(
        Enabled: false,
        MicrophoneEnabled: false,
        SystemAudioEnabled: false,
        AnalysisEnabled: false,
        PersistMicrophoneTranscriptExcerpt: false,
        PersistSystemAudioTranscriptExcerpt: true,
        TranscriptDetail: AudioTranscriptDetail.Detailed,
        ContextDepth: 5,
        TranscriptRetentionSeconds: 300,
        StoredObservationCount: 100,
        MinimumAnalysisConfidence: 0.60,
        AnalysisTimeoutSeconds: 45);

    public AudioContextSettings Normalize() => this with
    {
        TranscriptDetail = Enum.IsDefined(TranscriptDetail)
            ? TranscriptDetail
            : AudioTranscriptDetail.Detailed,
        ContextDepth = Math.Clamp(ContextDepth, 0, 20),
        TranscriptRetentionSeconds = Math.Clamp(TranscriptRetentionSeconds, 1, 3600),
        StoredObservationCount = Math.Clamp(StoredObservationCount, 1, 1000),
        MinimumAnalysisConfidence = Math.Clamp(MinimumAnalysisConfidence, 0, 1),
        AnalysisTimeoutSeconds = Math.Clamp(AnalysisTimeoutSeconds, 5, 180)
    };
}
