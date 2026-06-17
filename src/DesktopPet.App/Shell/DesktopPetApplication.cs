using DesktopPet.App.Cloud;
using DesktopPet.App.Conversation;
using DesktopPet.App.Input;
using DesktopPet.App.Memory;
using DesktopPet.App.Overlay;
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
    private readonly CloudAiSettingsStore _cloudSettingsStore;
    private readonly UiSettingsStore _uiSettingsStore;
    private readonly PetProfileSettingsStore _profileSettingsStore;
    private readonly HttpClient _httpClient;
    private readonly IPetChatService _chatService;
    private readonly IVoiceSynthesisService _voiceSynthesisService;
    private readonly IPetMemoryStore _memoryStore;
    private readonly TempFileAudioPlayer _audioPlayer;
    private readonly PetOverlayWindow _overlayWindow;
    private readonly ConversationOverlayWindow _conversationOverlayWindow;
    private readonly PetConversationController _conversationController;
    private readonly PetTrayController _trayController;

    private SettingsWindow? _settingsWindow;
    private MemoryWindow? _memoryWindow;
    private GlobalHotkeyService? _chatHotkeyService;

    public DesktopPetApplication(WpfApplication application)
    {
        _application = application;

        _cloudSettingsStore = new CloudAiSettingsStore();
        _uiSettingsStore = new UiSettingsStore();
        _profileSettingsStore = new PetProfileSettingsStore();
        _httpClient = new HttpClient();
        _chatService = new ElevenLabsAgentChatService(_httpClient, _cloudSettingsStore.Load);
        _voiceSynthesisService = new ElevenLabsVoiceSynthesisService(_httpClient, _cloudSettingsStore.Load);
        _memoryStore = new LocalPetMemoryStore();
        _audioPlayer = new TempFileAudioPlayer();

        _overlayWindow = new PetOverlayWindow(new PetOverlayCommands(
            ShowChat,
            ShowSettings,
            ShowMemories,
            StartSpeak),
            SaveOverlayPosition);
        _conversationOverlayWindow = new ConversationOverlayWindow(_overlayWindow.GetScreenBounds);
        _conversationController = new PetConversationController(
            _conversationOverlayWindow,
            _chatService,
            _voiceSynthesisService,
            _profileSettingsStore.Load,
            _audioPlayer,
            _overlayWindow);
        _trayController = new PetTrayController(
            _overlayWindow,
            ShowSettings,
            ShowChat,
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
                _cloudSettingsStore,
                _uiSettingsStore,
                _profileSettingsStore,
                ApplyUiSettings,
                GetHotkeyWarning);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ShowMemories()
    {
        if (_memoryWindow is null)
        {
            _memoryWindow = new MemoryWindow(_memoryStore);
            _memoryWindow.Closed += (_, _) => _memoryWindow = null;
        }

        _memoryWindow.Show();
        _memoryWindow.Activate();
    }

    private void ShowChat()
    {
        _conversationOverlayWindow.ToggleInput();
    }

    private static void StartSpeak()
    {
        // Voice input is intentionally a visible stub until the microphone path exists.
    }

    private string? ApplyUiSettings(UiSettings settings)
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

    private string? GetHotkeyWarning()
    {
        return _chatHotkeyService?.CurrentRegistrationError;
    }
}
