using System.Windows.Automation;

namespace DesktopPet.App.Observation;

internal sealed class UiAutomationContextCollector : IUiAutomationContextCollector
{
    private const int MaximumDepth = 4;
    private const int MaximumNodes = 80;
    private const int MaximumLabelLength = 120;
    private const int MaximumTotalTextLength = 800;
    private static readonly TimeSpan CollectionTimeout = TimeSpan.FromMilliseconds(750);

    private readonly IObservationPermissionService _permissionService;

    public UiAutomationContextCollector(IObservationPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    public async Task<(DesktopContextCollectionStatus Status, UiAutomationSnapshot? Snapshot)> CollectAsync(
        ForegroundWindowSnapshot window,
        CancellationToken cancellationToken)
    {
        if (!_permissionService.IsAllowed(window.ExecutablePath, DesktopContextCapabilities.Structure))
        {
            return (DesktopContextCollectionStatus.NotPermitted, null);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CollectionTimeout);

        try
        {
            return await Task.Run(() => Collect(window, timeout.Token), timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (DesktopContextCollectionStatus.TimedOut, null);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException
            or InvalidOperationException
            or UnauthorizedAccessException)
        {
            return (DesktopContextCollectionStatus.Unsupported, null);
        }
    }

    private static (DesktopContextCollectionStatus, UiAutomationSnapshot?) Collect(
        ForegroundWindowSnapshot window,
        CancellationToken cancellationToken)
    {
        var root = AutomationElement.FromHandle(window.WindowHandle);
        if (root is null)
        {
            return (DesktopContextCollectionStatus.Unsupported, null);
        }

        var labels = new List<string>();
        var totalTextLength = 0;
        var queue = new Queue<(AutomationElement Element, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = 0;

        while (queue.Count > 0 && visited < MaximumNodes && totalTextLength < MaximumTotalTextLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (element, depth) = queue.Dequeue();
            visited++;

            try
            {
                var current = element.Current;
                if (current.ProcessId != window.ProcessId || current.IsOffscreen || current.IsPassword)
                {
                    continue;
                }

                var name = Clean(current.Name);
                if (!string.IsNullOrWhiteSpace(name) && !labels.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    labels.Add(name);
                    totalTextLength += name.Length;
                }

                if (depth >= MaximumDepth)
                {
                    continue;
                }

                var child = TreeWalker.ControlViewWalker.GetFirstChild(element);
                while (child is not null && queue.Count + visited < MaximumNodes)
                {
                    queue.Enqueue((child, depth + 1));
                    child = TreeWalker.ControlViewWalker.GetNextSibling(child);
                }
            }
            catch (ElementNotAvailableException)
            {
            }
        }

        string? focusedType = null;
        string? focusedName = null;
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is not null
                && focused.Current.ProcessId == window.ProcessId
                && !focused.Current.IsPassword)
            {
                focusedType = focused.Current.ControlType?.ProgrammaticName?.Replace("ControlType.", string.Empty);
                focusedName = Clean(focused.Current.Name);
            }
        }
        catch (ElementNotAvailableException)
        {
        }

        if (labels.Count == 0 && string.IsNullOrWhiteSpace(focusedName))
        {
            return (DesktopContextCollectionStatus.Empty, null);
        }

        return (
            DesktopContextCollectionStatus.Available,
            new UiAutomationSnapshot(focusedType, focusedName, labels, DateTimeOffset.UtcNow));
    }

    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value.Trim().ReplaceLineEndings(" ");
        return cleaned.Length <= MaximumLabelLength
            ? cleaned
            : string.Concat(cleaned.AsSpan(0, MaximumLabelLength - 3), "...");
    }
}
