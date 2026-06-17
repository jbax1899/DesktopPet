using System.Windows;

namespace DesktopPet.App.Memory;

public partial class MemoryWindow : Window
{
    private readonly IPetMemoryStore _memoryStore;
    private List<PetMemoryEntry> _memories = [];

    public MemoryWindow(IPetMemoryStore memoryStore)
    {
        _memoryStore = memoryStore;

        InitializeComponent();
        RefreshMemories("Memories loaded.");
    }

    private void OnAddClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = NewMemoryTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                StatusTextBlock.Text = "Type a memory first.";
                return;
            }

            _memoryStore.Add(text);
            NewMemoryTextBox.Clear();
            RefreshMemories("Memory added.");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Add failed: {ex.Message}";
        }
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        RefreshMemories("Memories refreshed.");
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (MemoryListBox.SelectedItem is not PetMemoryEntry selectedMemory)
        {
            return;
        }

        try
        {
            _memoryStore.Delete(selectedMemory.Id);
            RefreshMemories("Memory deleted.");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Delete failed: {ex.Message}";
        }
    }

    private void OnClearClicked(object sender, RoutedEventArgs e)
    {
        if (_memories.Count == 0)
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            "Clear all memories?",
            "Desktop Pet Memories",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _memoryStore.Clear();
            RefreshMemories("Memories cleared.");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Clear failed: {ex.Message}";
        }
    }

    private void OnMemorySelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        DeleteMemoryButton.IsEnabled = MemoryListBox.SelectedItem is PetMemoryEntry;
    }

    private void RefreshMemories(string successMessage)
    {
        try
        {
            _memories = _memoryStore.List().ToList();
            MemoryListBox.ItemsSource = _memories;
            DeleteMemoryButton.IsEnabled = false;
            ClearMemoriesButton.IsEnabled = _memories.Count > 0;
            StatusTextBlock.Text = _memories.Count == 0
                ? "No memories yet."
                : successMessage;
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Load failed: {ex.Message}";
        }
    }
}
