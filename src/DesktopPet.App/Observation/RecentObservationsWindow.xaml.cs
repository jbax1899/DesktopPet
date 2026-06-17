using System.Windows;
using DesktopPet.App.Memory;

namespace DesktopPet.App.Observation;

public partial class RecentObservationsWindow : Window
{
    private readonly IDesktopObservationCoordinator _coordinator;
    private readonly IMemoryStore _memoryStore;

    public RecentObservationsWindow(
        IDesktopObservationCoordinator coordinator,
        IMemoryStore memoryStore)
    {
        _coordinator = coordinator;
        _memoryStore = memoryStore;
        InitializeComponent();
        Refresh();
    }

    private void OnProposeMemoryClicked(object sender, RoutedEventArgs e)
    {
        if (ObservationsList.SelectedItem is not ReducedDesktopObservation observation)
        {
            return;
        }

        var proposed = $"Working in {observation.ApplicationName}: {observation.ActivityDescription}";
        var window = new ObservationMemoryCandidateWindow(_memoryStore, proposed)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        Refresh();
    }

    private void Refresh()
    {
        ObservationsList.ItemsSource = _coordinator.RecentObservations
            .OrderByDescending(item => item.ObservedAt)
            .ToArray();
    }
}
