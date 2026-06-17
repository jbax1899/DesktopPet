using DesktopPet.App.Cloud;
using DesktopPet.App.Conversation;
using DesktopPet.App.Errors;
using DesktopPet.App.Input;
using DesktopPet.App.Memory;
using DesktopPet.App.Overlay;
using DesktopPet.App.Observation;
using DesktopPet.App.Settings;
using DesktopPet.App.Tray;
using DesktopPet.App.Voice;
using System.Net.Http;
using System.Windows;
using WpfApplication = System.Windows.Application;

namespace DesktopPet.App.Shell;

public sealed class DesktopPetApplication : IDisposable
{
    private readonly WpfApplication _application;
    private readonly ElevenLabsSettingsStore _elevenLabsSettingsStore;
    private readonly UiSettingsStore _uiSettingsStore;
    private readonly ProfileSettingsStore _profileSettingsStore;
    private readonly CharacterErrorMessageStore _errorMessageStore;
    private readonly HttpClient _httpClient;
    private readonly IChatService _chatService;
    private readonly IVoiceSynthesisService _voiceSynthesisService;
    private readonly IMemoryStore _memoryStore;
    private readonly IChatHistoryStore _chatHistoryStore;
    private readonly ChatAudioStore _chatAudioStore;
    private readonly StreamingMp3AudioPlayer _audioPlayer;
    private readonly ForegroundDesktopContextProvider _desktopContextProvider;
    private readonly ObservationSettingsStore _observationSettingsStore;
    private readonly IObservationPermissionService _observationPermissionService;
    private readonly IForegroundWindowCollector _foregroundWindowCollector;
    private readonly PetOverlayWindow _overlayWindow;
    private readonly ConversationOverlayWindow _conversationOverlayWindow;
    private readonly ConversationController _conversationController;
    private readonly TrayController _trayController;

    private SettingsWindow? _settingsWindow;
    private ObservationSettingsWindow? _observationSettingsWindow;
    private MemoryWindow? _memoryWindow;
    private GlobalHotkeyService? _chatHotkeyService;

    public DesktopPetApplication(WpfApplication application)
    {
        _application = application;

        _elevenLabsSettingsStore = new ElevenLabsSettingsStore();
        _uiSettingsStore = new UiSettingsStore();
        _profileSettingsStore = new ProfileSettingsStore();
        _errorMessageStore = new CharacterErrorMessageStore();
        _httpClient = new HttpClient();
        _chatService = new ElevenLabsAgentChatService(_httpClient, _elevenLabsSettingsStore.Load);
        _voiceSynthesisService = new ElevenLabsVoiceSynthesisService(_httpClient, _elevenLabsSettingsStore.Load);
        _memoryStore = new LocalMemoryStore();
        _chatHistoryStore = new LocalChatHistoryStore();
        _chatAudioStore = new ChatAudioStore();
        _audioPlayer = new StreamingMp3AudioPlayer();
        _observationSettingsStore = new ObservationSettingsStore();
        _observationPermissionService = new ObservationPermissionService(_observationSettingsStore);
        _foregroundWindowCollector = new ForegroundWindowCollector(_observationPermissionService);
        _desktopContextProvider = new ForegroundDesktopContextProvider(
            _foregroundWindowCollector,
            _observationPermissionService);

        _overlayWindow = new PetOverlayWindow(new OverlayCommands(
            ShowChat,
            ShowSettings,
            ShowMemories,
            StartSpeak),
            SaveOverlayPosition);
        _conversationOverlayWindow = new ConversationOverlayWindow(_overlayWindow.GetScreenBounds);
        _conversationController = new ConversationController(
            _conversationOverlayWindow,
            _chatService,
            _voiceSynthesisService,
            _chatHistoryStore,
            _chatAudioStore,
            _profileSettingsStore.Load,
            _audioPlayer,
            _overlayWindow,
            _errorMessageStore,
            _memoryStore,
            _desktopContextProvider);
        _trayController = new TrayController(
            _overlayWindow,
            ShowSettings,
            _application.Shutdown);

        _application.MainWindow = _overlayWindow;
    }

    public void Start()
    {
        var uiSettings = _uiSettingsStore.Load();
        _overlayWindow.SetInitialPosition(uiSettings.OverlayPosition);
        _overlayWindow.Show();
        _chatHotkeyService = new GlobalHotkeyService(_overlayWindow, ShowChat);
        ApplyUiSettings(uiSettings);
    }

    public void Dispose()
    {
        _settingsWindow?.Close();
        _observationSettingsWindow?.Close();
        _memoryWindow?.Close();
        _conversationOverlayWindow.Close();
        _conversationController.Dispose();
        _chatHotkeyService?.Dispose();
        _trayController.Dispose();
        _audioPlayer.Dispose();
        _httpClient.Dispose();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(
                _elevenLabsSettingsStore,
                _uiSettingsStore,
                _profileSettingsStore,
                _errorMessageStore,
                ApplyUiSettings,
                GetHotkeyWarning,
                ShowObservationSettings);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ShowObservationSettings()
    {
        if (_observationSettingsWindow is null)
        {
            _observationSettingsWindow = new ObservationSettingsWindow(_observationPermissionService);
            _observationSettingsWindow.Closed += (_, _) => _observationSettingsWindow = null;
        }

        _observationSettingsWindow.Owner = _settingsWindow;
        _observationSettingsWindow.Show();
        _observationSettingsWindow.Activate();
    }

    private void ShowMemories()
    {
        if (_memoryWindow is null)
        {
            _memoryWindow = new MemoryWindow(
                _memoryStore,
                _chatHistoryStore,
                _chatAudioStore,
                _conversationController.ReplayCachedSpeechAsync);
            _memoryWindow.Closed += (_, _) => _memoryWindow = null;
        }

        _memoryWindow.Show();
        _memoryWindow.Activate();
    }

    private void ShowChat()
    {
        _desktopContextProvider.PrepareCurrentContext();
        _conversationOverlayWindow.ToggleInput();
    }

    private static void StartSpeak()
    {
        // Voice input is intentionally a visible stub until the microphone path exists.
    }

    private PetError? ApplyUiSettings(UiSettings settings)
    {
        return _chatHotkeyService?.Register(settings.ChatShortcut);
    }

    private void SaveOverlayPosition(Rect bounds)
    {
        var settings = _uiSettingsStore.Load() with
        {
            OverlayPosition = new OverlayPosition(bounds.Left, bounds.Top)
        };

        _uiSettingsStore.Save(settings);
    }

    private PetError? GetHotkeyWarning()
    {
        return _chatHotkeyService?.CurrentRegistrationError;
    }
}
