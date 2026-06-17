using DesktopPet.App.Memory;
using System.Windows;

namespace DesktopPet.App.Observation;

public partial class ObservationMemoryCandidateWindow : Window
{
    private readonly IMemoryStore _memoryStore;

    public ObservationMemoryCandidateWindow(IMemoryStore memoryStore, string proposedText)
    {
        _memoryStore = memoryStore;
        InitializeComponent();
        CandidateTextBox.Text = proposedText;
        CandidateTextBox.SelectAll();
        CandidateTextBox.Focus();
    }

    private void OnAddClicked(object sender, RoutedEventArgs e)
    {
        var text = CandidateTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            System.Windows.MessageBox.Show(this, "Memory text cannot be blank.", Title);
            return;
        }

        try
        {
            _memoryStore.Add(text);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"Memory could not be saved: {ex.Message}", Title);
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
