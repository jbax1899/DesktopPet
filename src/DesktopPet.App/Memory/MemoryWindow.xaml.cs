using System.IO;
using System.Windows;
using System.Windows.Input;
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
    private readonly FileSystemWatcher _chatHistoryWatcher;
    private readonly FileSystemWatcher _memoriesWatcher;
    private readonly FileSystemWatcher _observationsWatcher;
    private bool _suppressWatcherEvents;
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

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet");

        _chatHistoryWatcher = CreateWatcher(dataDirectory, "chat-history.json", OnChatHistoryFileChanged);
        _memoriesWatcher = CreateWatcher(dataDirectory, "memories.json", OnMemoriesFileChanged);
        _observationsWatcher = CreateWatcher(dataDirectory, "observations.json", OnObservationsFileChanged);

        InitializeComponent();
        RefreshChatHistory();
        RefreshMemories();
        RefreshObservations();
    }

    protected override void OnClosed(EventArgs e)
    {
        _chatHistoryWatcher.Dispose();
        _memoriesWatcher.Dispose();
        _observationsWatcher.Dispose();
        base.OnClosed(e);
    }

    private async void OnPlayAudioClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: ChatHistoryMessage message })
        {
            return;
        }

        if (!_chatAudioStore.Exists(message.AudioFileName))
        {
            RefreshChatHistory();
            return;
        }

        try
        {
            await _playCachedAudio(message);
        }
        catch (Exception)
        {
            RefreshChatHistory();
        }
    }

    private void OnAddClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = NewMemoryTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            SuppressWatcherEvents();
            _memoryStore.Add(text);
            NewMemoryTextBox.Clear();
            RefreshMemories();
        }
        catch (Exception)
        {
        }
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (MemoryListBox.SelectedItem is not MemoryEntry selectedMemory)
        {
            return;
        }

        try
        {
            SuppressWatcherEvents();
            _memoryStore.Delete(selectedMemory.Id);
            RefreshMemories();
        }
        catch (Exception)
        {
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
            SuppressWatcherEvents();
            _memoryStore.Clear();
            RefreshMemories();
        }
        catch (Exception)
        {
        }
    }

    private void OnMemorySelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        DeleteMemoryButton.IsEnabled = MemoryListBox.SelectedItem is MemoryEntry;
    }

    private void OnTabChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
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
            SuppressWatcherEvents();
            _observationStore.Clear();
            _ambientDecisionStore.Clear();
            RefreshObservations();
        }
        catch (Exception)
        {
        }
    }

    private void RefreshChatHistory()
    {
        try
        {
            _chatMessages = _chatHistoryStore.List()
                .Select(message => new ChatHistoryMessageView(message, _chatAudioStore.Exists(message.AudioFileName)))
                .ToList();
            ChatHistoryListBox.ItemsSource = _chatMessages;

            if (_chatMessages.Count > 0)
            {
                ChatHistoryListBox.UpdateLayout();
                ChatHistoryListBox.ScrollIntoView(_chatMessages[^1]);
            }
        }
        catch (Exception)
        {
        }
    }

    private void RefreshMemories()
    {
        try
        {
            _memories = _memoryStore.List().ToList();
            MemoryListBox.ItemsSource = _memories;
            DeleteMemoryButton.IsEnabled = false;
            ClearMemoriesButton.IsEnabled = _memories.Count > 0;

            if (_memories.Count > 0)
            {
                MemoryListBox.UpdateLayout();
                MemoryListBox.ScrollIntoView(_memories[^1]);
            }
        }
        catch (Exception)
        {
        }
    }

    private void RefreshObservations()
    {
        try
        {
            var observations = _observationStore.List();
            ObservationsListBox.ItemsSource = observations;

            if (observations.Count > 0)
            {
                ObservationsListBox.UpdateLayout();
                ObservationsListBox.ScrollIntoView(observations[^1]);
            }
        }
        catch (Exception)
        {
        }
    }

    private void SuppressWatcherEvents()
    {
        _suppressWatcherEvents = true;
        Dispatcher.BeginInvoke(new Action(() => _suppressWatcherEvents = false), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnChatHistoryFileChanged(object sender, FileSystemEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_suppressWatcherEvents)
            {
                RefreshChatHistory();
            }
        });
    }

    private void OnMemoriesFileChanged(object sender, FileSystemEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_suppressWatcherEvents)
            {
                RefreshMemories();
            }
        });
    }

    private void OnObservationsFileChanged(object sender, FileSystemEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_suppressWatcherEvents)
            {
                RefreshObservations();
            }
        });
    }

    private static FileSystemWatcher CreateWatcher(string directory, string fileName, FileSystemEventHandler handler)
    {
        Directory.CreateDirectory(directory);
        var watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        watcher.Changed += handler;
        return watcher;
    }

    private sealed record ChatHistoryMessageView(ChatHistoryMessage Message, bool HasAudio)
    {
        public string Text => Message.Text;

        public DateTime CreatedAtUtc => Message.CreatedAtUtc;

        public bool IsUser => Message.Role == ChatHistoryRole.User;

        public bool IsBot => Message.Role == ChatHistoryRole.Bot;
    }
}
