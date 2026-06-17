namespace DesktopPet.App.Observation;

internal sealed record UiAutomationSnapshot(
    string? FocusedControlType,
    string? FocusedControlName,
    IReadOnlyList<string> VisibleLabels,
    DateTimeOffset ObservedAt);

internal interface IUiAutomationContextCollector
{
    Task<(DesktopContextCollectionStatus Status, UiAutomationSnapshot? Snapshot)> CollectAsync(
        ForegroundWindowSnapshot window,
        CancellationToken cancellationToken);
}
