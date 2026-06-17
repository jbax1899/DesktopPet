using System.Windows;

namespace DesktopPet.App.Observation;

public partial class AmbientDecisionsWindow : Window
{
    private readonly AmbientDecisionStore _store;

    public AmbientDecisionsWindow(AmbientDecisionStore store)
    {
        _store = store;
        InitializeComponent();
        Refresh();
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e) => Refresh();

    private void OnClearClicked(object sender, RoutedEventArgs e)
    {
        _store.Clear();
        Refresh();
    }

    private void Refresh()
    {
        DecisionsList.ItemsSource = _store.List();
    }
}
