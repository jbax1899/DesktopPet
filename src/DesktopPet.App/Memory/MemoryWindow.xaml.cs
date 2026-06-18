using System.Windows;
using System.Windows.Controls;
using DesktopPet.App.Observation;

namespace DesktopPet.App.Memory;

public partial class MemoryWindow : Window
{
    private readonly IMemoryStore _memoryStore;
    private readonly IChatHistoryStore _chatHistoryStore;
    private readonly ChatAudioStore _chatAudioStore;
    private readonly Func<ChatHistoryMessage, Task> _playCachedAudio;
    private readonly IDesktopObservationCoordinator _observationCoordinator;
    private readonly AmbientDecisionStore _ambientDecisionStore;
    private readonly ObservationStore _observationStore;
    private List<MemoryEntry> _memories = [];
    private List<ChatHistoryMessageView> _chatMessages = [];

    public MemoryWindow(
        IMemoryStore memoryStore,
        IChatHistoryStore chatHistoryStore,
        ChatAudioStore chatAudioStore,
        Func<ChatHistoryMessage, Task> playCachedAudio,
        IDesktopObservationCoordinator observationCoordinator,
        AmbientDecisionStore ambientDecisionStore,
        ObservationStore observationStore)
    {
        _memoryStore = memoryStore;
        _chatHistoryStore = chatHistoryStore;
        _chatAudioStore = chatAudioStore;
        _playCachedAudio = playCachedAudio;
        _observationCoordinator = observationCoordinator;
        _ambientDecisionStore = ambientDecisionStore;
        _observationStore = observationStore;

        InitializeComponent();
        RefreshChatHistory("Chat history loaded.");
        RefreshMemories("Memories loaded.");
        RefreshObservations();
    }

    private async void OnPlayAudioClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: ChatHistoryMessage message })
        {
            return;
        }

        if (!_chatAudioStore.Exists(message.AudioFileName))
        {
            RefreshChatHistory("Cached audio is missing.");
            return;
        }

        try
        {
            ChatStatusTextBlock.Text = "Playing cached audio.";
            await _playCachedAudio(message);
            ChatStatusTextBlock.Text = "Playback started.";
        }
        catch (Exception ex)
        {
            ChatStatusTextBlock.Text = $"Playback failed: {ex.Message}";
            RefreshChatHistory(ChatStatusTextBlock.Text);
        }
    }

    private void OnRefreshChatClicked(object sender, RoutedEventArgs e)
    {
        RefreshChatHistory("Chat history refreshed.");
    }

    private void OnAddClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = NewMemoryTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                MemoryStatusTextBlock.Text = "Type a memory first.";
                return;
            }

            _memoryStore.Add(text);
            NewMemoryTextBox.Clear();
            RefreshMemories("Memory added.");
        }
        catch (Exception ex)
        {
            MemoryStatusTextBlock.Text = $"Add failed: {ex.Message}";
        }
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        RefreshMemories("Memories refreshed.");
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (MemoryListBox.SelectedItem is not MemoryEntry selectedMemory)
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
            MemoryStatusTextBlock.Text = $"Delete failed: {ex.Message}";
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
            MemoryStatusTextBlock.Text = $"Clear failed: {ex.Message}";
        }
    }

    private void OnMemorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DeleteMemoryButton.IsEnabled = MemoryListBox.SelectedItem is MemoryEntry;
    }

    private void OnRefreshObservationsClicked(object sender, RoutedEventArgs e)
    {
        RefreshObservations();
    }

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is not System.Windows.Controls.TabControl)
        {
            return;
        }

        ScrollActiveTabToBottom();
    }

    private void ScrollActiveTabToBottom()
    {
        var selectedIndex = MainTabControl.SelectedIndex;

        if (selectedIndex == 0 && _chatMessages.Count > 0)
        {
            ChatHistoryListBox.UpdateLayout();
            ChatHistoryListBox.ScrollIntoView(_chatMessages[^1]);
        }
        else if (selectedIndex == 1 && _memories.Count > 0)
        {
            MemoryListBox.UpdateLayout();
            MemoryListBox.ScrollIntoView(_memories[^1]);
        }
        else if (selectedIndex == 2)
        {
            var observations = ObservationsListBox.ItemsSource as IReadOnlyList<ObservationRecord>;
            if (observations is { Count: > 0 })
            {
                ObservationsListBox.UpdateLayout();
                ObservationsListBox.ScrollIntoView(observations[^1]);
            }
        }
    }

    private void OnClearObservationsClicked(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            this,
            "Clear all observation records?",
            "Desktop Pet Observations",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _observationStore.Clear();
            _ambientDecisionStore.Clear();
            RefreshObservations();
            ObservationsStatusTextBlock.Text = "Observations cleared.";
        }
        catch (Exception ex)
        {
            ObservationsStatusTextBlock.Text = $"Clear failed: {ex.Message}";
        }
    }

    private void RefreshChatHistory(string successMessage)
    {
        try
        {
            _chatMessages = _chatHistoryStore.List()
                .Select(message => new ChatHistoryMessageView(message, _chatAudioStore.Exists(message.AudioFileName)))
                .ToList();
            ChatHistoryListBox.ItemsSource = _chatMessages;
            ChatStatusTextBlock.Text = _chatMessages.Count == 0
                ? "No chat history yet."
                : successMessage;

            if (_chatMessages.Count > 0)
            {
                ChatHistoryListBox.UpdateLayout();
                ChatHistoryListBox.ScrollIntoView(_chatMessages[^1]);
            }
        }
        catch (Exception ex)
        {
            ChatStatusTextBlock.Text = $"Load failed: {ex.Message}";
        }
    }

    private void RefreshMemories(string successMessage)
    {
        try
        {
            _memories = _memoryStore.List().ToList();
            MemoryListBox.ItemsSource = _memories;
            DeleteMemoryButton.IsEnabled = false;
            ClearMemoriesButton.IsEnabled = _memories.Count > 0;
            MemoryStatusTextBlock.Text = _memories.Count == 0
                ? "No memories yet."
                : successMessage;

            if (_memories.Count > 0)
            {
                MemoryListBox.UpdateLayout();
                MemoryListBox.ScrollIntoView(_memories[^1]);
            }
        }
        catch (Exception ex)
        {
            MemoryStatusTextBlock.Text = $"Load failed: {ex.Message}";
        }
    }

    private void RefreshObservations()
    {
        try
        {
            var observations = _observationStore.List();
            ObservationsListBox.ItemsSource = observations;
            ObservationsStatusTextBlock.Text = observations.Count == 0
                ? "No observations yet."
                : $"{observations.Count} observation(s) recorded.";

            if (observations.Count > 0)
            {
                ObservationsListBox.UpdateLayout();
                ObservationsListBox.ScrollIntoView(observations[^1]);
            }
        }
        catch (Exception ex)
        {
            ObservationsStatusTextBlock.Text = $"Refresh failed: {ex.Message}";
        }
    }

    private sealed record ChatHistoryMessageView(ChatHistoryMessage Message, bool HasAudio)
    {
        public string Text => Message.Text;

        public DateTime CreatedAtUtc => Message.CreatedAtUtc;

        public bool IsUser => Message.Role == ChatHistoryRole.User;

        public bool IsBot => Message.Role == ChatHistoryRole.Bot;
    }
}
