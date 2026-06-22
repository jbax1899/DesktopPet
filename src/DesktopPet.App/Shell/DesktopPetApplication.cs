using DesktopPet.App.Audio;
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
    private readonly SettingsHub _settings;
    private readonly HttpClient _httpClient;
    private readonly CharacterErrorMessageStore _errorMessageStore;
    private readonly MemoryServiceFactory _memory;
    private readonly CloudServiceFactory _cloud;
    private readonly AudioServiceFactory _audio;
    private readonly ObservationServiceFactory _observation;
    private readonly SpeechPlayback _speechPlayback;
    private readonly PetOverlayWindow _overlayWindow;
    private readonly ConversationOverlayWindow _conversationOverlayWindow;
    private readonly ConversationController _conversationController;
    private readonly CommentaryCoordinator _commentaryCoordinator;
    private readonly TrayController _trayController;

    private SettingsWindow? _settingsWindow;
    private MemoryWindow? _memoryWindow;
    private GlobalHotkeyService? _chatHotkeyService;
    private PushToTalkHotkeyService? _pushToTalkHotkeyService;
    private IDisposable? _listeningMoodScope;
    private bool _isPushToTalkRecording;

    public DesktopPetApplication(WpfApplication application)
    {
        _application = application;

        _settings = new SettingsHub();
        _httpClient = new HttpClient();
        _errorMessageStore = new CharacterErrorMessageStore();

        _memory = new MemoryServiceFactory();
        _cloud = new CloudServiceFactory(_httpClient, _settings);
        _audio = new AudioServiceFactory(_httpClient, _settings);
        _observation = new ObservationServiceFactory(
            _settings, _httpClient,
            _cloud.ChatService,
            _memory.MemoryStore,
            _memory.ChatHistoryStore,
            _audio.AudioObservationContextProvider);

        _overlayWindow = new PetOverlayWindow(new OverlayCommands(
            ShowChat,
            ShowSettings,
            ShowMemories,
            StartSpeak),
            SaveOverlayPosition);
        _conversationOverlayWindow = new ConversationOverlayWindow(_overlayWindow.GetScreenBounds);
        _speechPlayback = new SpeechPlayback(
            _audio.AudioPlayer,
            _audio.AudioCaptureCoordinator,
            _overlayWindow,
            _observation.AmbientActivityState,
            _memory.ChatHistoryStore,
            _memory.ChatAudioStore);
        _conversationController = new ConversationController(
            _conversationOverlayWindow,
            _cloud.ChatService,
            _cloud.VoiceSynthesisService,
            _memory.ChatHistoryStore,
            _memory.ChatAudioStore,
            _settings.Profile.Load,
            _speechPlayback,
            _overlayWindow,
            _errorMessageStore,
            _memory.MemoryStore,
            _observation.DesktopContextProvider,
            _observation.AmbientActivityState,
            _observation.ObservationStore,
            _observation.ObservationPermissionService,
            _audio.AudioObservationContextProvider.GetCurrentContext,
            _audio.AudioSegmentAnalyzer);
        _commentaryCoordinator = new CommentaryCoordinator(
            _observation.EnvironmentCoordinator,
            _observation.ObservationPermissionService,
            _observation.AmbientCommentPolicy,
            _observation.AmbientCommentGenerator,
            _cloud.VoiceSynthesisService,
            _speechPlayback,
            _conversationOverlayWindow,
            _observation.AmbientActivityState,
            _observation.AmbientDecisionStore,
            _observation.ObservationStore,
            _memory.ChatHistoryStore,
            _observation.ForegroundWindowCollector,
            _observation.WindowCaptureService,
            _observation.VisualContextAnalyzer);
        _trayController = new TrayController(
            _overlayWindow,
            ShowSettings,
            _application.Shutdown);

        _application.MainWindow = _overlayWindow;
    }

    public void Start()
    {
        var uiSettings = _settings.Ui.Load();
        _overlayWindow.SetInitialPosition(uiSettings.OverlayPosition);
        _overlayWindow.Show();
        _chatHotkeyService = new GlobalHotkeyService(_overlayWindow, ShowChat);
        _pushToTalkHotkeyService = new PushToTalkHotkeyService(
            uiSettings.PushToTalkShortcut,
            OnPushToTalkKeyPressed,
            OnPushToTalkKeyReleased);
        _pushToTalkHotkeyService.EnsureHookInstalled();
        ApplyUiSettings(uiSettings);
        _observation.EnvironmentCoordinator.Start();
        var audioSettings = _settings.AudioContext.Load();
        _audio.AudioCaptureCoordinator.ApplySettings(audioSettings);
        _audio.AudioCaptureCoordinator.ApplyPerAppCaptures(
            audioSettings.AudioApplicationRules,
            audioSettings.SystemAudioEnabled,
            TimeSpan.FromSeconds(audioSettings.MaximumSegmentDurationSeconds));
    }

    public void Dispose()
    {
        _settingsWindow?.Close();
        _memoryWindow?.Close();
        _commentaryCoordinator.Dispose();
        _observation.Dispose();
        _conversationOverlayWindow.Close();
        _conversationController.Dispose();
        _chatHotkeyService?.Dispose();
        _pushToTalkHotkeyService?.Dispose();
        _listeningMoodScope?.Dispose();
        _trayController.Dispose();
        _audio.Dispose();
        _memory.Dispose();
        _httpClient.Dispose();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(
                _settings,
                _cloud.PronunciationService,
                _cloud.OpenRouterModelsService,
                _cloud.CreditInfoService,
                _audio.AudioCaptureCoordinator,
                _audio.AudioObservationStore,
                _errorMessageStore,
                ApplyUiSettings,
                GetHotkeyWarning,
                _observation.ObservationPermissionService,
                _observation.ObservationStore,
                _observation.AmbientDecisionStore,
                _observation.EnvironmentCoordinator);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ShowMemories()
    {
        if (_memoryWindow is null)
        {
            _memoryWindow = new MemoryWindow(
                _memory.MemoryStore,
                _memory.ChatHistoryStore,
                _memory.ChatAudioStore,
                _conversationController.ReplayCachedSpeechAsync,
                _observation.EnvironmentCoordinator,
                _observation.AmbientDecisionStore,
                _observation.ObservationStore,
                _audio.AudioObservationStore,
                _audio.AudioAnalysisCoordinator,
                _observation.ObservationPermissionService,
                _settings.Profile.Load,
                _settings.Ui.Load,
                _audio.AudioObservationContextProvider.GetCurrentContext);
            _memoryWindow.Closed += (_, _) => _memoryWindow = null;
        }

        _memoryWindow.Show();
        _memoryWindow.Activate();
    }

    private void ShowChat()
    {
        _observation.DesktopContextProvider.PrepareCurrentContext();
        _conversationOverlayWindow.ToggleInput();
    }

    private void StartSpeak()
    {
        if (_isPushToTalkRecording)
        {
            OnPushToTalkKeyReleased();
        }
        else
        {
            OnPushToTalkKeyPressed();
        }
    }

    private void OnPushToTalkKeyPressed()
    {
        if (_isPushToTalkRecording)
        {
            return;
        }

        _observation.DesktopContextProvider.PrepareCurrentContext();
        _audio.AudioCaptureCoordinator.StartPushToTalkRecording();
        _isPushToTalkRecording = true;
        _listeningMoodScope = _overlayWindow.BeginMood(PetMood.Listening);
    }

    private async void OnPushToTalkKeyReleased()
    {
        if (!_isPushToTalkRecording)
        {
            return;
        }

        _isPushToTalkRecording = false;
        _listeningMoodScope?.Dispose();
        _listeningMoodScope = null;

        var segment = _audio.AudioCaptureCoordinator.StopPushToTalkRecording();
        if (segment is null)
        {
            return;
        }

        await _conversationController.SubmitVoiceInputAsync(segment, CancellationToken.None);
    }

    private PetError? ApplyUiSettings(UiSettings settings)
    {
        _pushToTalkHotkeyService?.ApplyShortcut(settings.PushToTalkShortcut);
        return _chatHotkeyService?.Register(settings.ChatShortcut);
    }

    private void SaveOverlayPosition(Rect bounds)
    {
        var settings = _settings.Ui.Load() with
        {
            OverlayPosition = new OverlayPosition(bounds.Left, bounds.Top)
        };

        _settings.Ui.Save(settings);
    }

    private PetError? GetHotkeyWarning()
    {
        return _chatHotkeyService?.CurrentRegistrationError;
    }
}
