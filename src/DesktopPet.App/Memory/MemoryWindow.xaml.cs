using System.IO;
using System.Windows;
using System.Windows.Input;
using DesktopPet.App.Cloud;
using DesktopPet.App.Observation;
using DesktopPet.App.Settings;
using WpfButton = System.Windows.Controls.Button;

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
    private readonly Func<ProfileSettings> _profileSettingsProvider;
    private readonly Func<UiSettings> _uiSettingsProvider;
    private readonly FileSystemWatcher _observationsWatcher;
    private readonly FileSystemWatcher _ambientDecisionsWatcher;
    private bool _suppressWatcherEvents;
    private List<MemoryEntry> _memories = [];
    private List<ChatHistoryMessageView> _chatMessages = [];
    private List<ObservationListItemView> _observationItems = [];
    private ContextInspectorWindow? _contextInspectorWindow;

    public MemoryWindow(
        IMemoryStore memoryStore,
        IChatHistoryStore chatHistoryStore,
        ChatAudioStore chatAudioStore,
        Func<ChatHistoryMessage, Task> playCachedAudio,
        IDesktopObservationCoordinator observationCoordinator,
        AmbientDecisionStore ambientDecisionStore,
        ObservationStore observationStore,
        Func<ProfileSettings> profileSettingsProvider,
        Func<UiSettings> uiSettingsProvider)
    {
        _memoryStore = memoryStore;
        _chatHistoryStore = chatHistoryStore;
        _chatAudioStore = chatAudioStore;
        _playCachedAudio = playCachedAudio;
        _observationCoordinator = observationCoordinator;
        _ambientDecisionStore = ambientDecisionStore;
        _observationStore = observationStore;
        _profileSettingsProvider = profileSettingsProvider;
        _uiSettingsProvider = uiSettingsProvider;

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet");

        _observationsWatcher = CreateWatcher(dataDirectory, "observations.json", OnObservationsFileChanged);
        _ambientDecisionsWatcher = CreateWatcher(dataDirectory, "ambient-decisions.json", OnObservationsFileChanged);
        _chatHistoryStore.Changed += OnChatHistoryChanged;
        _memoryStore.Changed += OnMemoriesChanged;

        InitializeComponent();
        RefreshChatHistory();
        RefreshMemories();
        RefreshObservations();
    }

    protected override void OnClosed(EventArgs e)
    {
        _contextInspectorWindow?.Close();
        _chatHistoryStore.Changed -= OnChatHistoryChanged;
        _memoryStore.Changed -= OnMemoriesChanged;
        _observationsWatcher.Dispose();
        _ambientDecisionsWatcher.Dispose();
        base.OnClosed(e);
    }

    private void OnContextPreviewClicked(object sender, RoutedEventArgs e)
    {
        var selectedMessage = (ChatHistoryListBox.SelectedItem as ChatHistoryMessageView)?.Message;
        if (_contextInspectorWindow is null)
        {
            _contextInspectorWindow = new ContextInspectorWindow(
                BuildLiveContextSnapshot,
                selectedMessage)
            {
                Owner = this
            };
            _contextInspectorWindow.Closed += (_, _) => _contextInspectorWindow = null;
        }
        else
        {
            _contextInspectorWindow.SetSelectedMessage(selectedMessage);
        }

        _contextInspectorWindow.Show();
        _contextInspectorWindow.Activate();
    }

    private void OnChatHistorySelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var selectedMessage = (ChatHistoryListBox.SelectedItem as ChatHistoryMessageView)?.Message;
        _contextInspectorWindow?.SetSelectedMessage(selectedMessage);
    }

    private AgentContextSnapshot BuildLiveContextSnapshot()
    {
        var memories = _memoryStore.List();
        var memoriesContext = memories.Count == 0
            ? null
            : string.Join("\n", memories.Select(memory => memory.Text));
        var observations = _observationStore.List()
            .OrderByDescending(record => record.CapturedAt)
            .Take(5)
            .ToArray();
        var request = new ChatRequest(
            string.Empty,
            _profileSettingsProvider(),
            memoriesContext,
            DesktopContext: null,
            ObservationHistory: observations,
            ConversationHistory: _chatHistoryStore.List());

        return AgentContextBuilder.Build(
            request,
            _uiSettingsProvider().GetEffectiveChatHistoryContext());
    }

    private async void OnPlayAudioClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: ChatHistoryMessage message })
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

    private void OnDesktopContextClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { ContextMenu: { } contextMenu } button)
        {
            return;
        }

        contextMenu.DataContext = button.DataContext;
        contextMenu.PlacementTarget = button;
        contextMenu.IsOpen = true;
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

            _memoryStore.Add(text);
            NewMemoryTextBox.Clear();
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
            _memoryStore.Delete(selectedMemory.Id);
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
            _memoryStore.Clear();
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
            if (_observationItems.Count > 0)
            {
                ObservationsListBox.UpdateLayout();
                ObservationsListBox.ScrollIntoView(_observationItems[^1]);
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
            System.Windows.MessageBox.Show(
                this,
                "Desktop Pet could not clear every observation file. Close anything using the local data files and try again.",
                "Desktop Pet Observations",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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
            var visualObservations = _observationStore.List();
            var ambientDecisions = _ambientDecisionStore.List()
                .Where(decision => !HasMatchingVisualObservation(decision, visualObservations))
                .Select(ObservationListItemView.FromAmbientDecision);

            _observationItems = visualObservations
                .Select(ObservationListItemView.FromVisualObservation)
                .Concat(ambientDecisions)
                .OrderBy(item => item.CapturedAt)
                .ToList();
            ObservationsListBox.ItemsSource = _observationItems;

            if (_observationItems.Count > 0)
            {
                ObservationsListBox.UpdateLayout();
                ObservationsListBox.ScrollIntoView(_observationItems[^1]);
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

    private void OnChatHistoryChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            RefreshChatHistory();
        });
    }

    private void OnMemoriesChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            RefreshMemories();
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
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        watcher.Changed += handler;
        watcher.Created += handler;
        watcher.Deleted += handler;
        watcher.Renamed += (sender, args) => handler(sender, args);
        return watcher;
    }

    private static bool HasMatchingVisualObservation(
        AmbientDecisionRecord decision,
        IReadOnlyList<ObservationRecord> visualObservations)
    {
        var application = ObservationListItemView.ExtractApplication(decision.Observation);
        return visualObservations.Any(observation =>
            string.Equals(observation.Application, application, StringComparison.OrdinalIgnoreCase)
            && Math.Abs((observation.CapturedAt - decision.CreatedAt).TotalSeconds) <= 5);
    }

    private sealed record ChatHistoryMessageView(ChatHistoryMessage Message, bool HasAudio)
    {
        private static readonly char[] LineSeparators = ['\r', '\n'];

        public string Text => Message.Text;

        public DateTime CreatedAtUtc => Message.CreatedAtUtc;

        public bool IsUser => Message.Role == ChatHistoryRole.User;

        public bool IsBot => Message.Role == ChatHistoryRole.Bot;

        public string? DesktopContext => Message.DesktopContext;

        public bool HasDesktopContext => !string.IsNullOrWhiteSpace(DesktopContext);

        public IReadOnlyList<DesktopContextField> DesktopContextFields =>
            string.IsNullOrWhiteSpace(DesktopContext)
                ? []
                : DesktopContext
                    .Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(ToDesktopContextField)
                    .ToArray();

        private static DesktopContextField ToDesktopContextField(string line)
        {
            var separatorIndex = line.IndexOf(':');
            return separatorIndex <= 0
                ? new DesktopContextField("Context", line)
                : new DesktopContextField(
                    line[..separatorIndex].Trim(),
                    line[(separatorIndex + 1)..].Trim());
        }
    }

    private sealed record DesktopContextField(string Label, string Value);

    private sealed record ObservationListItemView(
        string Application,
        string? Activity,
        string Summary,
        string Status,
        DateTimeOffset CapturedAt,
        ObservationOutcome Outcome,
        string? ThumbnailPath)
    {
        public static ObservationListItemView FromVisualObservation(ObservationRecord observation)
        {
            return new ObservationListItemView(
                observation.Application,
                observation.WindowTitle,
                observation.Analysis.Summary,
                FormatOutcome(observation.Outcome),
                observation.CapturedAt,
                observation.Outcome,
                observation.ThumbnailPath);
        }

        public static ObservationListItemView FromAmbientDecision(AmbientDecisionRecord decision)
        {
            var application = ExtractApplication(decision.Observation);
            return new ObservationListItemView(
                application,
                "Metadata observation",
                ExtractSummary(decision.Observation),
                decision.Spoke
                    ? "Spoke"
                    : $"Stayed quiet: {FormatReason(decision.Reason)}",
                decision.CreatedAt,
                MapOutcome(decision),
                null);
        }

        public static string ExtractApplication(string description)
        {
            var separatorIndex = description.IndexOf(':');
            return separatorIndex > 0
                ? description[..separatorIndex].Trim()
                : "Desktop";
        }

        private static string ExtractSummary(string description)
        {
            var separatorIndex = description.IndexOf(':');
            return separatorIndex >= 0 && separatorIndex + 1 < description.Length
                ? description[(separatorIndex + 1)..].Trim()
                : description;
        }

        private static ObservationOutcome MapOutcome(AmbientDecisionRecord decision)
        {
            if (decision.Spoke)
            {
                return ObservationOutcome.Spoken;
            }

            return decision.Reason switch
            {
                AmbientDecisionReason.CooldownActive => ObservationOutcome.Cooldown,
                AmbientDecisionReason.DuplicateTopic => ObservationOutcome.Duplicate,
                AmbientDecisionReason.UserRequestActive => ObservationOutcome.UserBusy,
                AmbientDecisionReason.SpeechActive => ObservationOutcome.UserBusy,
                AmbientDecisionReason.UserRecentlyTyping => ObservationOutcome.UserBusy,
                AmbientDecisionReason.PermissionRemoved => ObservationOutcome.Sensitive,
                _ => ObservationOutcome.BelowThreshold
            };
        }

        private static string FormatOutcome(ObservationOutcome outcome)
        {
            return outcome switch
            {
                ObservationOutcome.BelowThreshold => "Stayed quiet: below threshold",
                ObservationOutcome.Cooldown => "Stayed quiet: cooldown active",
                ObservationOutcome.Duplicate => "Stayed quiet: duplicate topic",
                ObservationOutcome.UserBusy => "Stayed quiet: user busy",
                ObservationOutcome.Stale => "Stayed quiet: stale",
                ObservationOutcome.Sensitive => "Stayed quiet: permission or sensitivity",
                _ => "Spoke"
            };
        }

        private static string FormatReason(AmbientDecisionReason reason)
        {
            return reason switch
            {
                AmbientDecisionReason.ObservationPaused => "observation paused",
                AmbientDecisionReason.AmbientDisabled => "ambient comments disabled",
                AmbientDecisionReason.PermissionRemoved => "permission removed",
                AmbientDecisionReason.UserRequestActive => "user request active",
                AmbientDecisionReason.SpeechActive => "speech already active",
                AmbientDecisionReason.UserRecentlyTyping => "user recently typing",
                AmbientDecisionReason.CooldownActive => "cooldown active",
                AmbientDecisionReason.DuplicateTopic => "duplicate topic",
                AmbientDecisionReason.GeneratorChoseSilence => "generator chose silence",
                AmbientDecisionReason.GenerationFailed => "generation failed",
                AmbientDecisionReason.BelowThreshold => "below threshold",
                _ => "eligible"
            };
        }
    }
}
