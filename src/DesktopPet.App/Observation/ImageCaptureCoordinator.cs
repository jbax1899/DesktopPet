namespace DesktopPet.App.Observation;

internal sealed class ImageCaptureCoordinator : IVisionObservationProvider, IDisposable
{
    private readonly IDesktopEnvironmentCaptureCoordinator _observationCoordinator;
    private readonly IObservationPermissionService _permissionService;
    private readonly IForegroundWindowCollector _foregroundWindowCollector;
    private readonly IWindowCaptureService _windowCaptureService;
    private readonly IVisualContextAnalyzer _visualAnalyzer;
    private readonly ObservationStore _observationStore;
    private readonly IAmbientCommentPolicy _policy;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _shutdown = new();

    private long _captureTurnId;
    private VisionObservation? _latestObservation;
    private string? _latestThumbnailPath;
    private long _latestObservationTurnId = -1;
    private bool _disposed;

    public ImageCaptureCoordinator(
        IDesktopEnvironmentCaptureCoordinator observationCoordinator,
        IObservationPermissionService permissionService,
        IForegroundWindowCollector foregroundWindowCollector,
        IWindowCaptureService windowCaptureService,
        IVisualContextAnalyzer visualAnalyzer,
        ObservationStore observationStore,
        IAmbientCommentPolicy policy)
    {
        _observationCoordinator = observationCoordinator;
        _permissionService = permissionService;
        _foregroundWindowCollector = foregroundWindowCollector;
        _windowCaptureService = windowCaptureService;
        _visualAnalyzer = visualAnalyzer;
        _observationStore = observationStore;
        _policy = policy;

        _observationCoordinator.ChangeDetected += OnChangeDetected;
    }

    public VisionObservation? LatestObservation
    {
        get
        {
            lock (_sync)
            {
                return _latestObservation;
            }
        }
    }

    public string? LatestThumbnailPath
    {
        get
        {
            lock (_sync)
            {
                return _latestThumbnailPath;
            }
        }
    }

    public IReadOnlyList<ObservationRecord> RecentObservations
    {
        get
        {
            try
            {
                return _observationStore.List()
                    .OrderByDescending(r => r.CapturedAt)
                    .Take(_permissionService.Current.ObservationContextDepth)
                    .ToArray();
            }
            catch
            {
                return [];
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _observationCoordinator.ChangeDetected -= OnChangeDetected;
        Interlocked.Increment(ref _captureTurnId);
        _shutdown.Cancel();
        _shutdown.Dispose();
        _disposed = true;
    }

    private async void OnChangeDetected(object? sender, DesktopObservationChange change)
    {
        if (_disposed)
        {
            return;
        }

        if (!_visualAnalyzer.IsAvailable
            || !_permissionService.IsAllowed(change.Observation.ExecutablePath, DesktopContextCapabilities.Visual))
        {
            return;
        }

        var turnId = Interlocked.Increment(ref _captureTurnId);
        System.Diagnostics.Debug.WriteLine($"ImageCapture: Change detected for {change.Observation.ApplicationName} ({change.Type}). Turn={turnId}.");

        try
        {
            await CaptureAndAnalyzeAsync(change, turnId, _shutdown.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ImageCapture: Vision analysis failed ({ex.GetType().Name}): {ex.Message}");
        }
    }

    private async Task CaptureAndAnalyzeAsync(
        DesktopObservationChange change,
        long turnId,
        CancellationToken cancellationToken)
    {
        if (change.Type is DesktopObservationChangeType.ForegroundApplicationChanged
            or DesktopObservationChangeType.WindowTitleChanged)
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(_permissionService.Current.ScreenshotCaptureDelayMilliseconds),
                cancellationToken);
            if (turnId != Volatile.Read(ref _captureTurnId))
            {
                return;
            }
        }

        var foregroundSnapshot = _foregroundWindowCollector.CollectPermittedMetadata();
        if (foregroundSnapshot is null)
        {
            return;
        }

        if (!string.Equals(foregroundSnapshot.ExecutablePath, change.Observation.ExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var capture = await _windowCaptureService.CaptureAsync(
            foregroundSnapshot.WindowHandle,
            foregroundSnapshot.ExecutablePath,
            foregroundSnapshot.IsVisible,
            foregroundSnapshot.IsMinimized,
            cancellationToken);

        using (capture.Image)
        {
            if (capture.Status != DesktopContextCollectionStatus.Available || capture.Image is null)
            {
                return;
            }

            var lastSpokeAt = _policy.GetLastSpokenAt();
            var recentObservations = _observationCoordinator.RecentObservations;
            var visionObservation = await _visualAnalyzer.AnalyzeDetailedAsync(
                capture.Image,
                new VisualAnalysisRequest(change.Observation.ApplicationName, change.Observation.ActivityDescription),
                recentObservations,
                lastSpokeAt,
                cancellationToken);

            if (visionObservation is null || turnId != Volatile.Read(ref _captureTurnId))
            {
                return;
            }

            string? thumbnailPath = null;
            try
            {
                var thumbnailId = Guid.NewGuid().ToString("N");
                thumbnailPath = _observationStore.SaveThumbnail(capture.Image.Bitmap, thumbnailId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save thumbnail ({ex.GetType().Name}): {ex.Message}");
            }

            lock (_sync)
            {
                if (turnId == Volatile.Read(ref _captureTurnId))
                {
                    _latestObservation = visionObservation;
                    _latestThumbnailPath = thumbnailPath;
                    _latestObservationTurnId = turnId;
                }
            }

            System.Diagnostics.Debug.WriteLine($"ImageCapture: Vision analysis complete. Summary={visionObservation.Summary}");
        }
    }

    internal sealed record CapturedAnalysis(VisionObservation Observation, string? ThumbnailPath);
}
