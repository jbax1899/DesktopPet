namespace DesktopPet.App.Audio;

public sealed record AudioContextSettings(
    bool Enabled,
    bool MicrophoneEnabled,
    bool SystemAudioEnabled,
    bool AnalysisEnabled,
    bool PersistMicrophoneTranscriptExcerpt,
    bool PersistSystemAudioTranscriptExcerpt,
    int ContextDepth,
    int TranscriptRetentionSeconds,
    int StoredObservationCount,
    double MinimumAnalysisConfidence,
    int AnalysisTimeoutSeconds,
    int TranscriptVerbosityLevel)
{
    public AudioContextSettings(bool enabled, bool microphoneEnabled, bool systemAudioEnabled)
        : this(
            enabled,
            microphoneEnabled,
            systemAudioEnabled,
            Default.AnalysisEnabled,
            Default.PersistMicrophoneTranscriptExcerpt,
            Default.PersistSystemAudioTranscriptExcerpt,
            Default.ContextDepth,
            Default.TranscriptRetentionSeconds,
            Default.StoredObservationCount,
            Default.MinimumAnalysisConfidence,
            Default.AnalysisTimeoutSeconds,
            Default.TranscriptVerbosityLevel)
    {
    }

    public static AudioContextSettings Default { get; } = new(
        Enabled: false,
        MicrophoneEnabled: false,
        SystemAudioEnabled: false,
        AnalysisEnabled: false,
        PersistMicrophoneTranscriptExcerpt: false,
        PersistSystemAudioTranscriptExcerpt: true,
        ContextDepth: 5,
        TranscriptRetentionSeconds: 300,
        StoredObservationCount: 100,
        MinimumAnalysisConfidence: 0.60,
        AnalysisTimeoutSeconds: 45,
        TranscriptVerbosityLevel: 5);

    public AudioContextSettings Normalize() => this with
    {
        ContextDepth = Math.Clamp(ContextDepth, 0, 20),
        TranscriptRetentionSeconds = Math.Clamp(TranscriptRetentionSeconds, 1, 3600),
        StoredObservationCount = Math.Clamp(StoredObservationCount, 1, 1000),
        MinimumAnalysisConfidence = Math.Clamp(MinimumAnalysisConfidence, 0, 1),
        AnalysisTimeoutSeconds = Math.Clamp(AnalysisTimeoutSeconds, 5, 180),
        TranscriptVerbosityLevel = Math.Clamp(TranscriptVerbosityLevel, 1, 10)
    };
}
