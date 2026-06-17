using System.Windows;
using DesktopPet.App.Memory;

namespace DesktopPet.App.Observation;

public partial class AmbientDecisionsWindow : Window
{
    private readonly AmbientDecisionStore _store;
    private readonly IMemoryStore _memoryStore;

    public AmbientDecisionsWindow(AmbientDecisionStore store, IMemoryStore memoryStore)
    {
        _store = store;
        _memoryStore = memoryStore;
        InitializeComponent();
        Refresh();
    }

    private void OnProposeMemoryClicked(object sender, RoutedEventArgs e)
    {
        if (DecisionsList.SelectedItem is not AmbientDecisionRecord decision)
        {
            return;
        }

        var window = new ObservationMemoryCandidateWindow(_memoryStore, decision.Observation)
        {
            Owner = this
        };
        window.ShowDialog();
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
