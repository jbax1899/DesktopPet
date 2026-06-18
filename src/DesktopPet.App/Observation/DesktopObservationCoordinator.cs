using System.Runtime.InteropServices;

namespace DesktopPet.App.Observation;

public enum DesktopObservationChangeType
{
    ForegroundApplicationChanged,
    WindowTitleChanged,
    DialogOrErrorAppeared,
    DialogOrErrorDisappeared,
    TaskCompleted,
    UserReturned,
    LongRunningActivity
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
}

internal sealed partial class DesktopObservationCoordinator : IDesktopObservationCoordinator
{
    private const int MaximumObservations = 50;
    private static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IForegroundWindowCollector _collector;
    private readonly IObservationPermissionService _permissionService;
    private readonly IUiAutomationContextCollector _uiAutomationCollector;
    private readonly object _sync = new();
    private readonly List<ReducedDesktopObservation> _recent = [];
    private readonly CancellationTokenSource _shutdown = new();

    private Task? _loopTask;
    private ReducedDesktopObservation? _previous;
    private DateTimeOffset _activityStartedAt;
    private bool _wasIdle;
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
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
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
            foreach (var change in DetectChanges(_previous, observation, primaryChange))
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
            var cutoff = observation.ObservedAt - MaximumAge;
            _recent.RemoveAll(item => item.ObservedAt < cutoff);
            _recent.Add(observation);
            if (_recent.Count > MaximumObservations)
            {
                _recent.RemoveRange(0, _recent.Count - MaximumObservations);
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
            return previous is null ? null : DesktopObservationChangeType.ForegroundApplicationChanged;
        }

        if (!string.Equals(previous.ActivityDescription, activity, StringComparison.Ordinal))
        {
            if (observedAt - _activityStartedAt < minimumDwell)
            {
                return null;
            }

            _activityStartedAt = observedAt;
            return DesktopObservationChangeType.WindowTitleChanged;
        }

        return null;
    }

    private IEnumerable<DesktopObservationChange> DetectChanges(
        ReducedDesktopObservation? previous,
        ReducedDesktopObservation current,
        DesktopObservationChangeType? primaryChange)
    {
        if (primaryChange is not null)
        {
            yield return CreateChange(primaryChange.Value, current);
        }

        var previousAttention = ContainsAttentionText(previous);
        var currentAttention = ContainsAttentionText(current);
        if (!previousAttention && currentAttention)
        {
            yield return CreateChange(DesktopObservationChangeType.DialogOrErrorAppeared, current);
        }
        else if (previousAttention && !currentAttention)
        {
            yield return CreateChange(DesktopObservationChangeType.DialogOrErrorDisappeared, current);
        }

        if (ContainsCompletionText(current) && !ContainsCompletionText(previous))
        {
            yield return CreateChange(DesktopObservationChangeType.TaskCompleted, current);
        }

        var isIdle = GetIdleDuration() >= TimeSpan.FromMinutes(5);
        if (_wasIdle && !isIdle)
        {
            yield return CreateChange(DesktopObservationChangeType.UserReturned, current);
        }

        _wasIdle = isIdle;
        if (current.ObservedAt - _activityStartedAt >= TimeSpan.FromMinutes(30)
            && previous is not null
            && previous.ObservedAt - _activityStartedAt < TimeSpan.FromMinutes(30))
        {
            yield return CreateChange(DesktopObservationChangeType.LongRunningActivity, current);
        }
    }

    private bool CanInspectStructure(string executablePath, DateTimeOffset now)
    {
        if (!_permissionService.IsAllowed(executablePath, DesktopContextCapabilities.Structure))
        {
            return false;
        }

        if (_lastStructuralInspection.TryGetValue(executablePath, out var last)
            && now - last < TimeSpan.FromSeconds(10))
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

    private static bool ContainsAttentionText(ReducedDesktopObservation? observation)
    {
        var text = $"{observation?.ActivityDescription} {observation?.StructuralDescription}";
        return text.Contains("error", StringComparison.OrdinalIgnoreCase)
            || text.Contains("warning", StringComparison.OrdinalIgnoreCase)
            || text.Contains("dialog", StringComparison.OrdinalIgnoreCase)
            || text.Contains("failed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsCompletionText(ReducedDesktopObservation? observation)
    {
        var text = $"{observation?.ActivityDescription} {observation?.StructuralDescription}";
        return text.Contains("complete", StringComparison.OrdinalIgnoreCase)
            || text.Contains("succeeded", StringComparison.OrdinalIgnoreCase)
            || text.Contains("finished", StringComparison.OrdinalIgnoreCase)
            || text.Contains("done", StringComparison.OrdinalIgnoreCase);
    }

    private static TimeSpan GetIdleDuration()
    {
        var info = new LastInputInfo
        {
            Size = (uint)Marshal.SizeOf<LastInputInfo>()
        };
        if (!NativeMethods.GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        var elapsedMilliseconds = unchecked((uint)Environment.TickCount - info.Time);
        return TimeSpan.FromMilliseconds(elapsedMilliseconds);
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetLastInputInfo(ref LastInputInfo info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }
}
