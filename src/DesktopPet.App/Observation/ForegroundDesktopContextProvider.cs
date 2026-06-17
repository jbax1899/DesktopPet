namespace DesktopPet.App.Observation;

internal sealed class ForegroundDesktopContextProvider : IDesktopContextProvider
{
    private const int MaximumActivityLength = 160;

    private readonly IForegroundWindowCollector _collector;
    private readonly IObservationPermissionService _permissionService;
    private readonly object _sync = new();

    private ForegroundWindowSnapshot? _preparedSnapshot;
    private string? _activeExecutablePath;
    private DateTimeOffset _activeSince;

    public ForegroundDesktopContextProvider(
        IForegroundWindowCollector collector,
        IObservationPermissionService permissionService)
    {
        _collector = collector;
        _permissionService = permissionService;
    }

    public void PrepareCurrentContext()
    {
        var snapshot = _collector.CollectPermittedMetadata();
        lock (_sync)
        {
            _preparedSnapshot = snapshot;
            if (snapshot is null)
            {
                return;
            }

            if (!string.Equals(_activeExecutablePath, snapshot.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                _activeExecutablePath = snapshot.ExecutablePath;
                _activeSince = snapshot.ObservedAt;
            }
        }
    }

    public Task<DesktopContextResult> GetCurrentContextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ForegroundWindowSnapshot? snapshot;
        DateTimeOffset activeSince;
        lock (_sync)
        {
            snapshot = _preparedSnapshot;
            _preparedSnapshot = null;
            activeSince = _activeSince;
        }

        if (!_permissionService.Current.ObservationEnabled)
        {
            return Task.FromResult(DesktopContextResult.NoContext(DesktopContextCollectionStatus.Disabled));
        }

        if (snapshot is null)
        {
            return Task.FromResult(DesktopContextResult.NoContext(DesktopContextCollectionStatus.Empty));
        }

        if (!_permissionService.IsAllowed(snapshot.ExecutablePath, DesktopContextCapabilities.Metadata))
        {
            return Task.FromResult(DesktopContextResult.NoContext(DesktopContextCollectionStatus.NotPermitted));
        }

        var rule = _permissionService.FindRule(snapshot.ExecutablePath);
        var applicationName = rule?.DisplayName;
        if (string.IsNullOrWhiteSpace(applicationName))
        {
            applicationName = snapshot.ProcessName;
        }

        var activity = snapshot.WindowTitle.Trim().ReplaceLineEndings(" ");
        if (activity.Length > MaximumActivityLength)
        {
            activity = string.Concat(activity.AsSpan(0, MaximumActivityLength - 3), "...");
        }

        var visibility = snapshot.IsMinimized
            ? "Minimized"
            : snapshot.IsVisible ? "Visible" : "Not visible";

        var context = new DesktopTurnContext(
            applicationName,
            string.IsNullOrWhiteSpace(activity) ? visibility : $"{activity} ({visibility})",
            snapshot.ObservedAt - activeSince,
            DesktopContextCapabilities.Metadata);

        return Task.FromResult(DesktopContextResult.Available(context));
    }
}
