using System.Windows;

namespace DesktopPet.App.Observation;

public partial class RecentObservationsWindow : Window
{
    private readonly IDesktopObservationCoordinator _coordinator;

    public RecentObservationsWindow(IDesktopObservationCoordinator coordinator)
    {
        _coordinator = coordinator;
        InitializeComponent();
        Refresh();
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
