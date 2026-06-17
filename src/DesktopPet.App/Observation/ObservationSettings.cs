namespace DesktopPet.App.Observation;

public enum CommentaryLevel
{
    Quiet,
    Balanced,
    Talkative
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
    bool DoNotDisturb,
    CommentaryLevel CommentaryLevel,
    IReadOnlyList<ApplicationObservationRule> ApplicationRules)
{
    public static ObservationSettings Default { get; } = new(
        ObservationEnabled: false,
        AmbientCommentsEnabled: false,
        DoNotDisturb: false,
        CommentaryLevel.Balanced,
        []);
}
