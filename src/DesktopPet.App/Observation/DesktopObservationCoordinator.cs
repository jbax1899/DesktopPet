namespace DesktopPet.App.Observation;

public enum DesktopObservationChangeType
{
    ForegroundApplicationChanged,
    WindowTitleChanged,
    CheckIn
}

public sealed record ReducedDesktopObservation(
    string ExecutablePath,
    string ApplicationName,
    string ActivityDescription,
    DateTimeOffset ObservedAt,
    DesktopContextCapabilities Capabilities,
    string? StructuralDescription = null);

public sealed record DesktopObservationChange(
    DesktopObservationChangeType Type,
    ReducedDesktopObservation Observation,
    string TopicKey);

public interface IDesktopObservationCoordinator : IDisposable
{
    event EventHandler<ReducedDesktopObservation>? ObservationAdded;

    event EventHandler<DesktopObservationChange>? ChangeDetected;

    IReadOnlyList<ReducedDesktopObservation> RecentObservations { get; }

    void Start();
    void ApplySettings();
}

internal sealed partial class DesktopObservationCoordinator : IDesktopObservationCoordinator
{
    private readonly IForegroundWindowCollector _collector;
    private readonly IObservationPermissionService _permissionService;
    private readonly IUiAutomationContextCollector _uiAutomationCollector;
    private readonly object _sync = new();
    private readonly List<ReducedDesktopObservation> _recent = [];
    private readonly CancellationTokenSource _shutdown = new();

    private Task? _loopTask;
    private ReducedDesktopObservation? _previous;
    private DateTimeOffset _activityStartedAt;
    private DateTimeOffset _lastCheckInAt;
    private readonly Dictionary<string, DateTimeOffset> _lastStructuralInspection = new(StringComparer.OrdinalIgnoreCase);

    public DesktopObservationCoordinator(
        IForegroundWindowCollector collector,
        IObservationPermissionService permissionService,
        IUiAutomationContextCollector uiAutomationCollector)
    {
        _collector = collector;
        _permissionService = permissionService;
        _uiAutomationCollector = uiAutomationCollector;
    }

    public event EventHandler<ReducedDesktopObservation>? ObservationAdded;

    public event EventHandler<DesktopObservationChange>? ChangeDetected;

    public IReadOnlyList<ReducedDesktopObservation> RecentObservations
    {
        get
        {
            lock (_sync)
            {
                return _recent.ToArray();
            }
        }
    }

    public void Start()
    {
        _loopTask ??= Task.Run(() => ObserveAsync(_shutdown.Token));
    }

    public void ApplySettings()
    {
        lock (_sync)
        {
            var settings = _permissionService.Current;
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(settings.RecentObservationAgeSeconds);
            _recent.RemoveAll(item => item.ObservedAt < cutoff);
            if (_recent.Count > settings.RecentObservationCount)
            {
                _recent.RemoveRange(0, _recent.Count - settings.RecentObservationCount);
            }
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        try
        {
            _loopTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        _shutdown.Dispose();
    }

    private async Task ObserveAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(
                TimeSpan.FromSeconds(_permissionService.Current.PollIntervalSeconds),
                cancellationToken);
            if (!_permissionService.Current.ObservationEnabled)
            {
                continue;
            }

            var snapshot = _collector.CollectPermittedMetadata();
            if (snapshot is null)
            {
                continue;
            }

            var rule = _permissionService.FindRule(snapshot.ExecutablePath);
            var applicationName = string.IsNullOrWhiteSpace(rule?.DisplayName)
                ? snapshot.ProcessName
                : rule.DisplayName;
            var activity = snapshot.WindowTitle.Trim().ReplaceLineEndings(" ");
            if (activity.Length > 160)
            {
                activity = string.Concat(activity.AsSpan(0, 157), "...");
            }

            var now = snapshot.ObservedAt;
            var primaryChange = DetectPrimaryChange(_previous, applicationName, activity, now);
            string? structuralDescription = null;
            var capabilities = DesktopContextCapabilities.Metadata;
            if (primaryChange is not null
                && CanInspectStructure(snapshot.ExecutablePath, now))
            {
                var structural = await _uiAutomationCollector.CollectAsync(snapshot, cancellationToken);
                if (structural.Status == DesktopContextCollectionStatus.Available
                    && structural.Snapshot is not null)
                {
                    structuralDescription = ReduceStructure(structural.Snapshot);
                    capabilities |= DesktopContextCapabilities.Structure;
                }
            }

            var observation = new ReducedDesktopObservation(
                snapshot.ExecutablePath,
                applicationName,
                activity,
                now,
                capabilities,
                structuralDescription);
            Add(observation);
            ObservationAdded?.Invoke(this, observation);
            foreach (var change in DetectChanges(observation, primaryChange))
            {
                ChangeDetected?.Invoke(this, change);
            }

            _previous = observation;
        }
    }

    private void Add(ReducedDesktopObservation observation)
    {
        lock (_sync)
        {
            var settings = _permissionService.Current;
            var cutoff = observation.ObservedAt - TimeSpan.FromSeconds(settings.RecentObservationAgeSeconds);
            _recent.RemoveAll(item => item.ObservedAt < cutoff);
            _recent.Add(observation);
            if (_recent.Count > settings.RecentObservationCount)
            {
                _recent.RemoveRange(0, _recent.Count - settings.RecentObservationCount);
            }
        }
    }

    private DesktopObservationChangeType? DetectPrimaryChange(
        ReducedDesktopObservation? previous,
        string applicationName,
        string activity,
        DateTimeOffset observedAt)
    {
        var minimumDwell = TimeSpan.FromSeconds(_permissionService.Current.MinimumDwellTimeSeconds);

        if (previous is null
            || !string.Equals(previous.ApplicationName, applicationName, StringComparison.OrdinalIgnoreCase))
        {
            if (previous is not null && observedAt - _activityStartedAt < minimumDwell)
            {
                return null;
            }

            _activityStartedAt = observedAt;
            _lastCheckInAt = observedAt;
            return previous is null ? null : DesktopObservationChangeType.ForegroundApplicationChanged;
        }

        if (!string.Equals(previous.ActivityDescription, activity, StringComparison.Ordinal))
        {
            if (observedAt - _activityStartedAt < minimumDwell)
            {
                return null;
            }

            _activityStartedAt = observedAt;
            _lastCheckInAt = observedAt;
            return DesktopObservationChangeType.WindowTitleChanged;
        }

        return null;
    }

    private IEnumerable<DesktopObservationChange> DetectChanges(
        ReducedDesktopObservation current,
        DesktopObservationChangeType? primaryChange)
    {
        if (primaryChange is not null)
        {
            yield return CreateChange(primaryChange.Value, current);
        }

        var checkInInterval = TimeSpan.FromSeconds(_permissionService.Current.CheckInSeconds);
        if (current.ObservedAt - _lastCheckInAt >= checkInInterval)
        {
            _lastCheckInAt = current.ObservedAt;
            yield return CreateChange(DesktopObservationChangeType.CheckIn, current);
        }
    }

    private bool CanInspectStructure(string executablePath, DateTimeOffset now)
    {
        if (!_permissionService.IsAllowed(executablePath, DesktopContextCapabilities.Structure))
        {
            return false;
        }

        if (_lastStructuralInspection.TryGetValue(executablePath, out var last)
            && now - last < TimeSpan.FromSeconds(_permissionService.Current.StructureInspectionCooldownSeconds))
        {
            return false;
        }

        _lastStructuralInspection[executablePath] = now;
        return true;
    }

    private static string? ReduceStructure(UiAutomationSnapshot snapshot)
    {
        var labels = snapshot.VisibleLabels.Take(5).ToArray();
        var focused = string.Join(" — ", new[] { snapshot.FocusedControlType, snapshot.FocusedControlName }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.Join(". ", new[]
        {
            string.IsNullOrWhiteSpace(focused) ? null : $"Focused: {focused}",
            labels.Length == 0 ? null : $"Visible: {string.Join("; ", labels)}"
        }.Where(value => value is not null));
    }

    private static DesktopObservationChange CreateChange(
        DesktopObservationChangeType type,
        ReducedDesktopObservation observation)
    {
        var topic = $"{observation.ApplicationName}|{type}|{observation.ActivityDescription}".ToLowerInvariant();
        return new DesktopObservationChange(type, observation, topic);
    }

}
