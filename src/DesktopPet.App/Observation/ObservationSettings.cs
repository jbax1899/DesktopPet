namespace DesktopPet.App.Observation;

public enum CommentaryLevel
{
    Quiet,
    Balanced,
    Talkative
}

public enum VisionSensitivity
{
    Low,
    Medium,
    High
}

public sealed record ApplicationObservationRule(
    string ExecutablePath,
    string DisplayName,
    bool IsDenied = false,
    bool AllowMetadata = false,
    bool AllowStructure = false,
    bool AllowVisual = false);

public sealed record ObservationSettings(
    bool ObservationEnabled,
    bool AmbientCommentsEnabled,
    CommentaryLevel CommentaryLevel,
    VisionSensitivity VisionSensitivity,
    int MinimumDwellTimeSeconds,
    int VisionAnalysisCooldownSeconds,
    IReadOnlyList<ApplicationObservationRule> ApplicationRules)
{
    public static ObservationSettings Default { get; } = new(
        ObservationEnabled: false,
        AmbientCommentsEnabled: false,
        CommentaryLevel.Balanced,
        VisionSensitivity.Medium,
        MinimumDwellTimeSeconds: 15,
        VisionAnalysisCooldownSeconds: 30,
        []);
}
