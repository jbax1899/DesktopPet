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
using System.IO;
using System.Net.Http;
using System.Windows;
using WpfApplication = System.Windows.Application;

namespace DesktopPet.App.Shell;

public sealed class DesktopPetApplication : IDisposable
{
    private readonly WpfApplication _application;
    private readonly ElevenLabsSettingsStore _elevenLabsSettingsStore;
    private readonly OpenRouterSettingsStore _openRouterSettingsStore;
    private readonly UiSettingsStore _uiSettingsStore;
    private readonly ProfileSettingsStore _profileSettingsStore;
    private readonly AudioContextSettingsStore _audioContextSettingsStore;
    private readonly TranscriptWorkingBuffer _transcriptWorkingBuffer;
    private readonly AudioObservationStore _audioObservationStore;
    private readonly IAudioSegmentAnalyzer _audioSegmentAnalyzer;
    private readonly AudioAnalysisCoordinator _audioAnalysisCoordinator;
    private readonly AudioCaptureCoordinator _audioCaptureCoordinator;
    private readonly CharacterErrorMessageStore _errorMessageStore;
    private readonly HttpClient _httpClient;
    private readonly IChatService _chatService;
    private readonly IVoiceSynthesisService _voiceSynthesisService;
    private readonly DesktopPetDatabase _database;
    private readonly IMemoryStore _memoryStore;
    private readonly IChatHistoryStore _chatHistoryStore;
    private readonly ChatAudioStore _chatAudioStore;
    private readonly StreamingMp3AudioPlayer _audioPlayer;
    private readonly SpeechPlayback _speechPlayback;
    private readonly ForegroundDesktopContextProvider _desktopContextProvider;
    private readonly ObservationSettingsStore _observationSettingsStore;
    private readonly IObservationPermissionService _observationPermissionService;
    private readonly IForegroundWindowCollector _foregroundWindowCollector;
    private readonly IUiAutomationContextCollector _uiAutomationContextCollector;
    private readonly IWindowCaptureService _windowCaptureService;
    private readonly IVisualContextAnalyzer _visualContextAnalyzer;
    private readonly IDesktopObservationCoordinator _observationCoordinator;
    private readonly IAmbientActivityState _ambientActivityState;
    private readonly IAmbientCommentPolicy _ambientCommentPolicy;
    private readonly IAmbientCommentGenerator _ambientCommentGenerator;
    private readonly AmbientDecisionStore _ambientDecisionStore;
    private readonly ObservationStore _observationStore;
    private readonly OpenRouterModelsService _openRouterModelsService;
    private readonly CreditInfoService _creditInfoService;
    private readonly ElevenLabsPronunciationService _pronunciationService;
    private readonly PetOverlayWindow _overlayWindow;
    private readonly ConversationOverlayWindow _conversationOverlayWindow;
    private readonly ConversationController _conversationController;
    private readonly AmbientCommentCoordinator _ambientCommentCoordinator;
    private readonly TrayController _trayController;

    private SettingsWindow? _settingsWindow;
    private MemoryWindow? _memoryWindow;
    private GlobalHotkeyService? _chatHotkeyService;

    public DesktopPetApplication(WpfApplication application)
    {
        _application = application;

        _elevenLabsSettingsStore = new ElevenLabsSettingsStore();
        _openRouterSettingsStore = new OpenRouterSettingsStore();
        _uiSettingsStore = new UiSettingsStore();
        _profileSettingsStore = new ProfileSettingsStore();
        _audioContextSettingsStore = new AudioContextSettingsStore();
        _errorMessageStore = new CharacterErrorMessageStore();
        _database = new DesktopPetDatabase();
        _database.Initialize();
        _httpClient = new HttpClient();
        _transcriptWorkingBuffer = new TranscriptWorkingBuffer(
            () => TimeSpan.FromMinutes(
                _audioContextSettingsStore.Load().Normalize().TranscriptRetentionMinutes));
        _audioObservationStore = new AudioObservationStore(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopPet",
                "audio-observations.json"),
            () => _audioContextSettingsStore.Load().Normalize().StoredObservationCount);
        _audioSegmentAnalyzer = new OpenRouterAudioSegmentAnalyzer(
            _httpClient,
            _openRouterSettingsStore.Load,
            _audioContextSettingsStore.Load);
        _audioAnalysisCoordinator = new AudioAnalysisCoordinator(
            _audioSegmentAnalyzer,
            _transcriptWorkingBuffer,
            _audioObservationStore);
        _audioCaptureCoordinator = new AudioCaptureCoordinator(
            kind => kind switch
            {
                AudioSourceKind.Microphone => new MicrophoneCaptureSource(),
                AudioSourceKind.SystemAudio => new SystemLoopbackCaptureSource(),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            },
            _audioAnalysisCoordinator);
        _chatService = new ElevenLabsAgentChatService(
            _httpClient,
            _elevenLabsSettingsStore.Load,
            _uiSettingsStore.Load);
        _voiceSynthesisService = new ElevenLabsVoiceSynthesisService(_httpClient, _elevenLabsSettingsStore.Load);
        _memoryStore = new SqliteMemoryStore(_database);
        _chatHistoryStore = new SqliteChatHistoryStore(_database);
        _chatAudioStore = new ChatAudioStore();
        _audioPlayer = new StreamingMp3AudioPlayer();
        _observationSettingsStore = new ObservationSettingsStore();
        _observationPermissionService = new ObservationPermissionService(_observationSettingsStore);
        _foregroundWindowCollector = new ForegroundWindowCollector(_observationPermissionService);
        _uiAutomationContextCollector = new UiAutomationContextCollector(_observationPermissionService);
        _windowCaptureService = new WindowCaptureService(_observationPermissionService);
        _observationStore = new ObservationStore(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopPet"),
            () => _observationPermissionService.Current.StoredObservationCount);
        _visualContextAnalyzer = new OpenRouterVisionAnalyzer(_httpClient, _openRouterSettingsStore.Load, _observationPermissionService, _observationStore);
        _openRouterModelsService = new OpenRouterModelsService(_httpClient, _openRouterSettingsStore.Load);
        _creditInfoService = new CreditInfoService(_httpClient, _elevenLabsSettingsStore.Load, _openRouterSettingsStore.Load);
        _pronunciationService = new ElevenLabsPronunciationService(_httpClient);
        _observationCoordinator = new DesktopObservationCoordinator(
            _foregroundWindowCollector,
            _observationPermissionService,
            _uiAutomationContextCollector);
        _ambientActivityState = new AmbientActivityState();
        _ambientCommentPolicy = new AmbientCommentPolicy(
            _observationPermissionService,
            _ambientActivityState);
        _ambientCommentGenerator = new ElevenLabsAmbientCommentGenerator(
            _chatService,
            _observationStore,
            _chatHistoryStore,
            _memoryStore,
            _profileSettingsStore.Load,
            _observationPermissionService);
        _ambientDecisionStore = new AmbientDecisionStore(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopPet",
                "ambient-decisions.json"),
            () => _observationPermissionService.Current.StoredAmbientDecisionCount);
        _desktopContextProvider = new ForegroundDesktopContextProvider(
            _foregroundWindowCollector,
            _observationPermissionService,
            _uiAutomationContextCollector,
            _windowCaptureService,
            _visualContextAnalyzer);

        _overlayWindow = new PetOverlayWindow(new OverlayCommands(
            ShowChat,
            ShowSettings,
            ShowMemories,
            StartSpeak),
            SaveOverlayPosition);
        _conversationOverlayWindow = new ConversationOverlayWindow(_overlayWindow.GetScreenBounds);
        _speechPlayback = new SpeechPlayback(
            _audioPlayer,
            _overlayWindow,
            _ambientActivityState,
            _chatHistoryStore,
            _chatAudioStore);
        _conversationController = new ConversationController(
            _conversationOverlayWindow,
            _chatService,
            _voiceSynthesisService,
            _chatHistoryStore,
            _chatAudioStore,
            _profileSettingsStore.Load,
            _speechPlayback,
            _overlayWindow,
            _errorMessageStore,
            _memoryStore,
            _desktopContextProvider,
            _ambientActivityState,
            _observationStore,
            _observationPermissionService);
        _ambientCommentCoordinator = new AmbientCommentCoordinator(
            _observationCoordinator,
            _observationPermissionService,
            _ambientCommentPolicy,
            _ambientCommentGenerator,
            _voiceSynthesisService,
            _speechPlayback,
            _conversationOverlayWindow,
            _ambientActivityState,
            _ambientDecisionStore,
            _observationStore,
            _chatHistoryStore,
            _windowCaptureService,
            _visualContextAnalyzer);
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
        _observationCoordinator.Start();
        _audioCaptureCoordinator.ApplySettings(_audioContextSettingsStore.Load());
    }

    public void Dispose()
    {
        _settingsWindow?.Close();
        _memoryWindow?.Close();
        _ambientCommentCoordinator.Dispose();
        _conversationOverlayWindow.Close();
        _conversationController.Dispose();
        _observationCoordinator.Dispose();
        _audioCaptureCoordinator.Dispose();
        _audioAnalysisCoordinator.Dispose();
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
                _pronunciationService,
                _openRouterSettingsStore,
                _openRouterModelsService,
                _creditInfoService,
                _uiSettingsStore,
                _profileSettingsStore,
                _audioContextSettingsStore,
                _audioCaptureCoordinator,
                _audioAnalysisCoordinator,
                _audioObservationStore,
                _errorMessageStore,
                ApplyUiSettings,
                GetHotkeyWarning,
                _observationPermissionService,
                _observationStore,
                _ambientDecisionStore,
                _observationCoordinator);
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
                _memoryStore,
                _chatHistoryStore,
                _chatAudioStore,
                _conversationController.ReplayCachedSpeechAsync,
                _observationCoordinator,
                _ambientDecisionStore,
                _observationStore,
                _audioObservationStore,
                _audioAnalysisCoordinator,
                _observationPermissionService,
                _profileSettingsStore.Load,
                _uiSettingsStore.Load);
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
