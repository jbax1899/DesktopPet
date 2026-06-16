using DesktopPet.App.Cloud;
using DesktopPet.App.Chat;
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
    private readonly CloudAiSettingsStore _settingsStore;
    private readonly HttpClient _httpClient;
    private readonly IPetChatService _chatService;
    private readonly IVoiceSynthesisService _voiceSynthesisService;
    private readonly TempFileAudioPlayer _audioPlayer;
    private readonly PetOverlayWindow _overlayWindow;
    private readonly PetTrayController _trayController;

    private SettingsWindow? _settingsWindow;
    private ChatWindow? _chatWindow;

    public DesktopPetApplication(WpfApplication application)
    {
        _application = application;

        _settingsStore = new CloudAiSettingsStore();
        _httpClient = new HttpClient();
        _chatService = new ElevenLabsAgentChatService(_httpClient, _settingsStore.Load);
        _voiceSynthesisService = new ElevenLabsVoiceSynthesisService(_httpClient, _settingsStore.Load);
        _audioPlayer = new TempFileAudioPlayer();

        _overlayWindow = new PetOverlayWindow();
        _trayController = new PetTrayController(
            _overlayWindow,
            ShowSettings,
            ShowChat,
            _application.Shutdown);

        _application.MainWindow = _overlayWindow;
    }

    public void Start()
    {
        _overlayWindow.Show();
    }

    public void Dispose()
    {
        _settingsWindow?.Close();
        _chatWindow?.Close();
        _trayController.Dispose();
        _audioPlayer.Dispose();
        _httpClient.Dispose();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settingsStore);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ShowChat()
    {
        if (_chatWindow is null)
        {
            _chatWindow = new ChatWindow(_chatService, _voiceSynthesisService, _audioPlayer);
            _chatWindow.Closed += (_, _) => _chatWindow = null;
        }

        _chatWindow.Show();
        _chatWindow.Activate();
    }
}
