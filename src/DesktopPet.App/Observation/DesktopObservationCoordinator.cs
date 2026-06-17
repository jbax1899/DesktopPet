namespace DesktopPet.App.Observation;

public sealed record ReducedDesktopObservation(
    string ApplicationName,
    string ActivityDescription,
    DateTimeOffset ObservedAt,
    DesktopContextCapabilities Capabilities);

public interface IDesktopObservationCoordinator : IDisposable
{
    event EventHandler<ReducedDesktopObservation>? ObservationAdded;

    IReadOnlyList<ReducedDesktopObservation> RecentObservations { get; }

    void Start();
}

internal sealed class DesktopObservationCoordinator : IDesktopObservationCoordinator
{
    private const int MaximumObservations = 50;
    private static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IForegroundWindowCollector _collector;
    private readonly IObservationPermissionService _permissionService;
    private readonly object _sync = new();
    private readonly List<ReducedDesktopObservation> _recent = [];
    private readonly CancellationTokenSource _shutdown = new();

    private Task? _loopTask;

    public DesktopObservationCoordinator(
        IForegroundWindowCollector collector,
        IObservationPermissionService permissionService)
    {
        _collector = collector;
        _permissionService = permissionService;
    }

    public event EventHandler<ReducedDesktopObservation>? ObservationAdded;

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

            var observation = new ReducedDesktopObservation(
                applicationName,
                activity,
                snapshot.ObservedAt,
                DesktopContextCapabilities.Metadata);
            Add(observation);
            ObservationAdded?.Invoke(this, observation);
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
}
