namespace DesktopPet.App.Observation;

[Flags]
public enum DesktopContextCapabilities
{
    None = 0,
    Metadata = 1,
    Structure = 2,
    Visual = 4
}

public enum DesktopContextCollectionStatus
{
    Available,
    Disabled,
    NotPermitted,
    Unsupported,
    TimedOut,
    Unavailable,
    Empty
}

public sealed record DesktopTurnContext(
    string ApplicationName,
    string? ActivityDescription,
    TimeSpan? ActiveDuration,
    DesktopContextCapabilities Capabilities,
    string? StructuralDescription = null,
    string? VisualDescription = null);

public sealed record DesktopContextResult(
    DesktopContextCollectionStatus Status,
    DesktopTurnContext? Context)
{
    public static DesktopContextResult NoContext(DesktopContextCollectionStatus status) => new(status, null);

    public static DesktopContextResult Available(DesktopTurnContext context) =>
        new(DesktopContextCollectionStatus.Available, context);
}
