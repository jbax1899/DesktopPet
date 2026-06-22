namespace DesktopPet.App.Observation;

public sealed class ForegroundDesktopContextProvider : IDesktopContextProvider
{
    private const int MaximumActivityLength = 160;

    private readonly IForegroundWindowCollector _collector;
    private readonly IObservationPermissionService _permissionService;
    private readonly IUiAutomationContextCollector _uiAutomationCollector;
    private readonly IWindowCaptureService _windowCaptureService;
    private readonly IVisualContextAnalyzer _visualAnalyzer;
    private readonly object _sync = new();

    private ForegroundWindowSnapshot? _preparedSnapshot;
    private string? _activeExecutablePath;
    private DateTimeOffset _activeSince;

    public ForegroundDesktopContextProvider(
        IForegroundWindowCollector collector,
        IObservationPermissionService permissionService,
        IUiAutomationContextCollector uiAutomationCollector,
        IWindowCaptureService windowCaptureService,
        IVisualContextAnalyzer visualAnalyzer)
    {
        _collector = collector;
        _permissionService = permissionService;
        _uiAutomationCollector = uiAutomationCollector;
        _windowCaptureService = windowCaptureService;
        _visualAnalyzer = visualAnalyzer;
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

    public async Task<DesktopContextResult> GetCurrentContextAsync(CancellationToken cancellationToken)
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
            return DesktopContextResult.NoContext(DesktopContextCollectionStatus.Disabled);
        }

        if (snapshot is null)
        {
            return DesktopContextResult.NoContext(DesktopContextCollectionStatus.Empty);
        }

        if (!_permissionService.IsAllowed(snapshot.ExecutablePath, DesktopContextCapabilities.Metadata))
        {
            return DesktopContextResult.NoContext(DesktopContextCollectionStatus.NotPermitted);
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

        var capabilities = DesktopContextCapabilities.Metadata;
        string? structuralDescription = null;
        string? visualDescription = null;
        var structuralResult = await _uiAutomationCollector.CollectAsync(snapshot, cancellationToken);
        if (structuralResult.Status == DesktopContextCollectionStatus.Available
            && structuralResult.Snapshot is not null)
        {
            structuralDescription = ReduceStructure(structuralResult.Snapshot);
            if (!string.IsNullOrWhiteSpace(structuralDescription))
            {
                capabilities |= DesktopContextCapabilities.Structure;
            }
        }

        if (_permissionService.Current.CaptureScreenshotOnChatSend
            && _visualAnalyzer.IsAvailable
            && _permissionService.IsAllowed(snapshot.ExecutablePath, DesktopContextCapabilities.Visual))
        {
            var capture = await _windowCaptureService.CaptureAsync(
                snapshot.WindowHandle,
                snapshot.ExecutablePath,
                snapshot.IsVisible,
                snapshot.IsMinimized,
                cancellationToken);
            using (capture.Image)
            {
                if (capture.Status == DesktopContextCollectionStatus.Available && capture.Image is not null)
                {
                    var analysis = await _visualAnalyzer.AnalyzeAsync(
                        capture.Image,
                        new VisualAnalysisRequest(applicationName, activity),
                        cancellationToken);
                    if (analysis.Status == DesktopContextCollectionStatus.Available
                        && !string.IsNullOrWhiteSpace(analysis.Description))
                    {
                        visualDescription = analysis.Description;
                        capabilities |= DesktopContextCapabilities.Visual;
                    }
                }
            }
        }

        var context = new DesktopTurnContext(
            applicationName,
            string.IsNullOrWhiteSpace(activity) ? visibility : $"{activity} ({visibility})",
            snapshot.ObservedAt - activeSince,
            capabilities,
            structuralDescription,
            visualDescription);

        return DesktopContextResult.Available(context);
    }

    private static string? ReduceStructure(UiAutomationSnapshot snapshot)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(snapshot.FocusedControlType)
            || !string.IsNullOrWhiteSpace(snapshot.FocusedControlName))
        {
            parts.Add($"Focused: {string.Join(" — ", new[] { snapshot.FocusedControlType, snapshot.FocusedControlName }.Where(value => !string.IsNullOrWhiteSpace(value)))}");
        }

        var labels = snapshot.VisibleLabels
            .Where(label => !string.Equals(label, snapshot.FocusedControlName, StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToArray();
        if (labels.Length > 0)
        {
            parts.Add($"Visible: {string.Join("; ", labels)}");
        }

        return parts.Count == 0 ? null : string.Join(". ", parts);
    }
}
