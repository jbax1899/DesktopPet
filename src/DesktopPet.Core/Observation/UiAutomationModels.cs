namespace DesktopPet.App.Observation;

public sealed record UiAutomationSnapshot(
    string? FocusedControlType,
    string? FocusedControlName,
    IReadOnlyList<string> VisibleLabels,
    DateTimeOffset ObservedAt);

public interface IUiAutomationContextCollector
{
    Task<(DesktopContextCollectionStatus Status, UiAutomationSnapshot? Snapshot)> CollectAsync(
        ForegroundWindowSnapshot window,
        CancellationToken cancellationToken);
}
