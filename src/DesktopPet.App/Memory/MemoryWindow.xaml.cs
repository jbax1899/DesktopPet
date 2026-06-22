using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DesktopPet.App.Audio;
using DesktopPet.App.Cloud;
using DesktopPet.App.Observation;
using DesktopPet.App.Settings;
using WpfButton = System.Windows.Controls.Button;
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using WpfItemsControl = System.Windows.Controls.ItemsControl;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfListBoxItem = System.Windows.Controls.ListBoxItem;

namespace DesktopPet.App.Memory;

public partial class MemoryWindow : Window
{
    private static readonly string[] ContextVariableOrder =
    [
        "temporal_context",
        "pet_name",
        "user_name",
        "memories_context",
        "desktop_observation_history",
        "audio_observation_history",
        "conversation_history"
    ];

    private readonly IMemoryStore _memoryStore;
    private readonly IChatHistoryStore _chatHistoryStore;
    private readonly ChatAudioStore _chatAudioStore;
    private readonly Func<ChatHistoryMessage, Task> _playCachedAudio;
    private readonly IDesktopEnvironmentCaptureCoordinator _observationCoordinator;
    private readonly AmbientDecisionStore _ambientDecisionStore;
    private readonly ObservationStore _observationStore;
    private readonly AudioObservationStore _audioObservationStore;
    private readonly AudioAnalysisCoordinator _audioAnalysisCoordinator;
    private readonly IObservationPermissionService _observationPermissionService;
    private readonly Func<ProfileSettings> _profileSettingsProvider;
    private readonly Func<UiSettings> _uiSettingsProvider;
    private readonly Func<string?> _audioObservationContextProvider;
    private readonly FileSystemWatcher _observationsWatcher;
    private readonly FileSystemWatcher _ambientDecisionsWatcher;
    private bool _suppressWatcherEvents;
    private List<MemoryEntry> _memories = [];
    private List<ChatHistoryMessageView> _chatMessages = [];
    private List<ObservationListItemView> _observationItems = [];

    public MemoryWindow(
        IMemoryStore memoryStore,
        IChatHistoryStore chatHistoryStore,
        ChatAudioStore chatAudioStore,
        Func<ChatHistoryMessage, Task> playCachedAudio,
        IDesktopEnvironmentCaptureCoordinator observationCoordinator,
        AmbientDecisionStore ambientDecisionStore,
        ObservationStore observationStore,
        AudioObservationStore audioObservationStore,
        AudioAnalysisCoordinator audioAnalysisCoordinator,
        IObservationPermissionService observationPermissionService,
        Func<ProfileSettings> profileSettingsProvider,
        Func<UiSettings> uiSettingsProvider,
        Func<string?> audioObservationContextProvider)
    {
        _memoryStore = memoryStore;
        _chatHistoryStore = chatHistoryStore;
        _chatAudioStore = chatAudioStore;
        _playCachedAudio = playCachedAudio;
        _observationCoordinator = observationCoordinator;
        _ambientDecisionStore = ambientDecisionStore;
        _observationStore = observationStore;
        _audioObservationStore = audioObservationStore;
        _audioAnalysisCoordinator = audioAnalysisCoordinator;
        _observationPermissionService = observationPermissionService;
        _profileSettingsProvider = profileSettingsProvider;
        _uiSettingsProvider = uiSettingsProvider;
        _audioObservationContextProvider = audioObservationContextProvider;

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet");

        _observationsWatcher = CreateWatcher(dataDirectory, "observations.json", OnObservationsFileChanged);
        _ambientDecisionsWatcher = CreateWatcher(dataDirectory, "ambient-decisions.json", OnObservationsFileChanged);
        _chatHistoryStore.Changed += OnChatHistoryChanged;
        _memoryStore.Changed += OnMemoriesChanged;
        _audioObservationStore.Changed += OnAudioObservationsChanged;

        InitializeComponent();
        RefreshChatHistory();
        RefreshMemories();
        RefreshObservations();
        ShowLiveContextSnapshot();
    }

    protected override void OnClosed(EventArgs e)
    {
        _chatHistoryStore.Changed -= OnChatHistoryChanged;
        _memoryStore.Changed -= OnMemoriesChanged;
        _audioObservationStore.Changed -= OnAudioObservationsChanged;
        _observationsWatcher.Dispose();
        _ambientDecisionsWatcher.Dispose();
        base.OnClosed(e);
    }

    private void OnChatHistorySelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var selectedMessage = (ChatHistoryListBox.SelectedItem as ChatHistoryMessageView)?.Message;
        DeleteChatButton.IsEnabled = selectedMessage is not null;

        if (selectedMessage is null)
        {
            ShowLiveContextSnapshot();
            return;
        }

        ShowRecordedContextSnapshot(selectedMessage);
    }

    private AgentContextSnapshot BuildLiveContextSnapshot()
    {
        var memories = _memoryStore.List();
        var memoriesContext = memories.Count == 0
            ? null
            : string.Join("\n", memories.Select(memory => memory.Text));
        var observations = _observationStore.List()
            .OrderByDescending(record => record.CapturedAt)
            .Take(_observationPermissionService.Current.ObservationContextDepth)
            .ToArray();
        var request = new ChatRequest(
            string.Empty,
            _profileSettingsProvider(),
            memoriesContext,
            DesktopContext: null,
            ObservationHistory: observations,
            ConversationHistory: _chatHistoryStore.List(),
            AudioObservationHistory: _audioObservationContextProvider());

        return AgentContextBuilder.Build(
            request,
            _uiSettingsProvider().GetEffectiveChatHistoryContext());
    }

    private void ShowLiveContextSnapshot()
    {
        ContextVariablesItemsControl.FontStyle = FontStyles.Normal;
        ContextVariablesItemsControl.ItemsSource = ToContextVariables(BuildLiveContextSnapshot());
    }

    private void ShowRecordedContextSnapshot(ChatHistoryMessage message)
    {
        ContextVariablesItemsControl.FontStyle = FontStyles.Italic;

        if (message.Role != ChatHistoryRole.Bot)
        {
            ContextVariablesItemsControl.ItemsSource = Array.Empty<ContextVariableView>();
            return;
        }

        if (message.ContextSnapshot is null)
        {
            ContextVariablesItemsControl.ItemsSource = Array.Empty<ContextVariableView>();
            return;
        }

        ContextVariablesItemsControl.ItemsSource = ToContextVariables(message.ContextSnapshot);
    }

    private static IReadOnlyList<ContextVariableView> ToContextVariables(AgentContextSnapshot snapshot)
    {
        var requestedOrder = ContextVariableOrder
            .Select((name, index) => (name, index))
            .ToDictionary(item => item.name, item => item.index, StringComparer.Ordinal);

        return snapshot.Values
            .OrderBy(pair => requestedOrder.TryGetValue(pair.Key, out var index)
                ? index
                : ContextVariableOrder.Length)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ContextVariableView(pair.Key, pair.Value))
            .ToArray();
    }

    private void OnListBoxPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not WpfListBox listBox
            || e.OriginalSource is not DependencyObject source
            || WpfItemsControl.ContainerFromElement(listBox, source) is not WpfListBoxItem item
            || !item.IsSelected
            || IsInsideButton(source, item))
        {
            return;
        }

        listBox.SelectedItem = null;
        e.Handled = true;
    }

    private static bool IsInsideButton(DependencyObject source, DependencyObject item)
    {
        for (var current = source; current is not null && current != item;)
        {
            if (current is WpfButtonBase)
            {
                return true;
            }

            current = GetParent(current);
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject child)
    {
        return child switch
        {
            Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(child),
            FrameworkContentElement contentElement => contentElement.Parent,
            _ => LogicalTreeHelper.GetParent(child)
        };
    }

    private void OnDeleteChatClicked(object sender, RoutedEventArgs e)
    {
        if (ChatHistoryListBox.SelectedItem is not ChatHistoryMessageView selected)
        {
            return;
        }

        try
        {
            _chatHistoryStore.Delete(selected.Message.Id);
            _chatAudioStore.Delete(selected.Message.AudioFileName);
        }
        catch (Exception)
        {
        }
    }

    private void OnClearChatClicked(object sender, RoutedEventArgs e)
    {
        if (_chatMessages.Count == 0)
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            "Clear all chat history?",
            "Desktop Pet Chat History",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var messagesToClear = _chatMessages.ToArray();
            _chatHistoryStore.Clear();
            foreach (var message in messagesToClear)
            {
                _chatAudioStore.Delete(message.Message.AudioFileName);
            }
        }
        catch (Exception)
        {
        }
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
        var dialog = new AddMemoryWindow
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _memoryStore.Add(dialog.MemoryText);
        }
        catch (Exception)
        {
        }
    }

    private void OnDeleteMemoryClicked(object sender, RoutedEventArgs e)
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

    private void OnClearMemoriesClicked(object sender, RoutedEventArgs e)
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
        else if (selectedIndex == 1 && _observationItems.Count > 0)
        {
            ObservationsListBox.UpdateLayout();
            ObservationsListBox.ScrollIntoView(_observationItems[^1]);
        }
        else if (selectedIndex == 2 && _memories.Count > 0)
        {
            MemoryListBox.UpdateLayout();
            MemoryListBox.ScrollIntoView(_memories[^1]);
        }
    }

    private void OnObservationSelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        DeleteObservationButton.IsEnabled =
            ObservationsListBox.SelectedItem is ObservationListItemView;
    }

    private void OnDeleteObservationClicked(object sender, RoutedEventArgs e)
    {
        if (ObservationsListBox.SelectedItem is not ObservationListItemView selected)
        {
            return;
        }

        try
        {
            SuppressWatcherEvents();
            switch (selected.Source)
            {
                case ObservationRecord visual:
                    foreach (var ambient in _ambientDecisionStore.List()
                                 .Where(decision => HasMatchingVisualObservation(decision, [visual])))
                    {
                        _ambientDecisionStore.Delete(ambient);
                    }

                    _observationStore.Delete(visual.Id);
                    break;
                case AmbientDecisionRecord ambient:
                    _ambientDecisionStore.Delete(ambient);
                    break;
                case AudioObservation audio:
                    _audioAnalysisCoordinator.DeleteObservation(audio);
                    break;
            }

            RefreshObservations();
        }
        catch (Exception)
        {
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
            _audioAnalysisCoordinator.ClearObservations();
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
            DeleteChatButton.IsEnabled = false;
            ClearChatButton.IsEnabled = _chatMessages.Count > 0;
            ShowLiveContextSnapshot();

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

            RefreshLiveContextIfUnselected();
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
            var audioObservations = _audioObservationStore.List()
                .Select(ObservationListItemView.FromAudioObservation);

            _observationItems = visualObservations
                .Select(ObservationListItemView.FromVisualObservation)
                .Concat(ambientDecisions)
                .Concat(audioObservations)
                .OrderBy(item => item.CapturedAt)
                .ToList();
            ObservationsListBox.ItemsSource = _observationItems;
            DeleteObservationButton.IsEnabled = false;
            ClearObservationsButton.IsEnabled = _observationItems.Count > 0;

            if (_observationItems.Count > 0)
            {
                ObservationsListBox.UpdateLayout();
                ObservationsListBox.ScrollIntoView(_observationItems[^1]);
            }

            RefreshLiveContextIfUnselected();
        }
        catch (Exception)
        {
        }
    }

    private void RefreshLiveContextIfUnselected()
    {
        if (ChatHistoryListBox.SelectedItem is null)
        {
            ShowLiveContextSnapshot();
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

    private void OnAudioObservationsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(RefreshObservations);
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
        object Source,
        string Application,
        string? Activity,
        string Summary,
        string Status,
        DateTimeOffset CapturedAt,
        ObservationOutcome Outcome,
        string? ThumbnailPath,
        string? Excerpt)
    {
        public Visibility ExcerptVisibility =>
            string.IsNullOrWhiteSpace(Excerpt) ? Visibility.Collapsed : Visibility.Visible;

        public static ObservationListItemView FromVisualObservation(ObservationRecord observation)
        {
            return new ObservationListItemView(
                observation,
                observation.Application,
                observation.WindowTitle,
                observation.Analysis.Summary,
                FormatOutcome(observation.Outcome),
                observation.CapturedAt,
                observation.Outcome,
                observation.ThumbnailPath,
                null);
        }

        public static ObservationListItemView FromAmbientDecision(AmbientDecisionRecord decision)
        {
            var application = ExtractApplication(decision.Observation);
            return new ObservationListItemView(
                decision,
                application,
                "Metadata observation",
                ExtractSummary(decision.Observation),
                decision.Spoke
                    ? "Spoke"
                    : $"Stayed quiet: {FormatReason(decision.Reason)}",
                decision.CreatedAt,
                MapOutcome(decision),
                null,
                null);
        }

        public static ObservationListItemView FromAudioObservation(AudioObservation observation)
        {
            var source = observation.Source == AudioSourceKind.Microphone
                ? "Microphone audio"
                : "System audio";
            var transcriptStatus = !observation.TranscriptExpiresAt.HasValue
                ? "No full transcript retained"
                : observation.TranscriptExpiresAt.Value <= DateTimeOffset.UtcNow
                    ? "Temporary full transcript expired"
                    : $"Temporary full transcript expires by {observation.TranscriptExpiresAt.Value.LocalDateTime:g}; also clears on disable, clear, or restart";
            return new ObservationListItemView(
                observation,
                source,
                $"Transcript · {observation.Confidence:P0} confidence",
                observation.TranscriptExcerpt ?? "(no excerpt retained)",
                transcriptStatus,
                observation.CreatedAt,
                ObservationOutcome.Recorded,
                null,
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
                ObservationOutcome.Recorded => "Recorded",
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
