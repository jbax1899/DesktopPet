using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using DesktopPet.App.Audio;
using DesktopPet.App.Cloud;
using DesktopPet.App.Errors;
using DesktopPet.App.Observation;

namespace DesktopPet.App.Settings;

public partial class SettingsWindow : Window
{
    private readonly ElevenLabsSettingsStore _elevenLabsSettingsStore;
    private readonly ElevenLabsPronunciationService _pronunciationService;
    private readonly OpenRouterSettingsStore _openRouterSettingsStore;
    private readonly OpenRouterModelsService _openRouterModelsService;
    private readonly CreditInfoService _creditInfoService;
    private readonly UiSettingsStore _uiSettingsStore;
    private readonly ProfileSettingsStore _profileSettingsStore;
    private readonly AudioContextSettingsStore _audioContextSettingsStore;
    private readonly AudioCaptureCoordinator _audioCaptureCoordinator;
    private readonly AudioObservationStore _audioObservationStore;
    private readonly CharacterErrorMessageStore _errorMessageStore;
    private readonly Func<UiSettings, PetError?> _applyUiSettings;
    private readonly Func<PetError?> _getHotkeyWarning;
    private readonly IObservationPermissionService _permissionService;
    private readonly ObservationStore _observationStore;
    private readonly AmbientDecisionStore _ambientDecisionStore;
    private readonly IDesktopEnvironmentCaptureCoordinator _observationCoordinator;
    private readonly ObservableCollection<ApplicationRuleRow> _observationRows = [];
    private readonly ObservableCollection<OpenRouterModelInfo> _visionModels = [];
    private readonly ObservableCollection<OpenRouterModelInfo> _audioModels = [];
    private readonly DispatcherTimer _audioDiagnosticsTimer;
    private KeyboardShortcut _selectedChatShortcut = KeyboardShortcut.DefaultChatShortcut;
    private KeyboardShortcut _selectedPushToTalkShortcut = KeyboardShortcut.DefaultPushToTalkShortcut;
    private ShortcutTarget _recordingTarget;
    private bool _isRecordingShortcut;
    private bool _loadingSettings;
    private bool _loadingObservationSettings;
    private bool _syncingCommentThreshold;
    private bool _syncingVisionDetail;
    private bool _syncingVisionVerbosity;
    private bool _syncingTranscriptVerbosity;

    public SettingsWindow(
        ElevenLabsSettingsStore elevenLabsSettingsStore,
        ElevenLabsPronunciationService pronunciationService,
        OpenRouterSettingsStore openRouterSettingsStore,
        OpenRouterModelsService openRouterModelsService,
        CreditInfoService creditInfoService,
        UiSettingsStore uiSettingsStore,
        ProfileSettingsStore profileSettingsStore,
        AudioContextSettingsStore audioContextSettingsStore,
        AudioCaptureCoordinator audioCaptureCoordinator,
        AudioObservationStore audioObservationStore,
        CharacterErrorMessageStore errorMessageStore,
        Func<UiSettings, PetError?> applyUiSettings,
        Func<PetError?> getHotkeyWarning,
        IObservationPermissionService permissionService,
        ObservationStore observationStore,
        AmbientDecisionStore ambientDecisionStore,
        IDesktopEnvironmentCaptureCoordinator observationCoordinator)
    {
        _elevenLabsSettingsStore = elevenLabsSettingsStore;
        _pronunciationService = pronunciationService;
        _openRouterSettingsStore = openRouterSettingsStore;
        _openRouterModelsService = openRouterModelsService;
        _creditInfoService = creditInfoService;
        _uiSettingsStore = uiSettingsStore;
        _profileSettingsStore = profileSettingsStore;
        _audioContextSettingsStore = audioContextSettingsStore;
        _audioCaptureCoordinator = audioCaptureCoordinator;
        _audioObservationStore = audioObservationStore;
        _errorMessageStore = errorMessageStore;
        _applyUiSettings = applyUiSettings;
        _getHotkeyWarning = getHotkeyWarning;
        _permissionService = permissionService;
        _observationStore = observationStore;
        _ambientDecisionStore = ambientDecisionStore;
        _observationCoordinator = observationCoordinator;

        InitializeComponent();
        _audioDiagnosticsTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(500),
            DispatcherPriority.Background,
            OnAudioDiagnosticsTick,
            Dispatcher);
        ApplicationsGrid.ItemsSource = _observationRows;
        OpenRouterVisionModelComboBox.ItemsSource = _visionModels;
        OpenRouterAudioModelComboBox.ItemsSource = _audioModels;
        SubscribeSettingEvents();
        LoadSettings();
        _ = LoadVisionModelsAsync();
        _ = LoadAudioModelsAsync();
        _ = LoadCreditsAsync();
        _audioDiagnosticsTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        ApplyAllSettings();
        _audioDiagnosticsTimer.Stop();
        _audioDiagnosticsTimer.Tick -= OnAudioDiagnosticsTick;
        base.OnClosed(e);
    }

    private void SubscribeSettingEvents()
    {
        RoutedEventHandler settingChanged = OnSettingChanged;
        System.Windows.Controls.SelectionChangedEventHandler comboChanged = (_, _) => OnSettingChanged(this, EventArgs.Empty);

        // General
        UserNameTextBox.LostFocus += settingChanged;
        NicknameTextBox.LostFocus += settingChanged;

        // Cloud Providers
        ElevenLabsApiKeyPasswordBox.LostFocus += settingChanged;
        ElevenLabsAgentIdTextBox.LostFocus += settingChanged;
        ElevenLabsVoiceIdTextBox.LostFocus += settingChanged;
        OpenRouterApiKeyPasswordBox.LostFocus += settingChanged;
        OpenRouterVisionModelComboBox.SelectionChanged += comboChanged;
        OpenRouterAudioModelComboBox.SelectionChanged += comboChanged;
        OpenRouterRequireZdrCheckBox.Checked += settingChanged;
        OpenRouterRequireZdrCheckBox.Unchecked += settingChanged;

        // Audio
        MicrophoneDeviceComboBox.SelectionChanged += comboChanged;
        SystemAudioDeviceComboBox.SelectionChanged += comboChanged;
        TranscriptVerbositySlider.ValueChanged += (_, _) => OnSettingChanged(this, EventArgs.Empty);
        PersistMicrophoneExcerptCheckBox.Checked += settingChanged;
        PersistMicrophoneExcerptCheckBox.Unchecked += settingChanged;
        PersistSystemAudioExcerptCheckBox.Checked += settingChanged;
        PersistSystemAudioExcerptCheckBox.Unchecked += settingChanged;
        AudioContextDepthTextBox.LostFocus += settingChanged;
        TranscriptRetentionSecondsTextBox.LostFocus += settingChanged;
        StoredAudioObservationCountTextBox.LostFocus += settingChanged;
        MinimumAudioConfidenceTextBox.LostFocus += settingChanged;
        AudioAnalysisTimeoutSecondsTextBox.LostFocus += settingChanged;
        MaxSegmentDurationSecondsTextBox.LostFocus += settingChanged;

        // Vision
        CaptureScreenshotOnChatSendCheckBox.Checked += settingChanged;
        CaptureScreenshotOnChatSendCheckBox.Unchecked += settingChanged;
        VisionDetailSlider.ValueChanged += (_, _) => OnSettingChanged(this, EventArgs.Empty);
        VisionVerbositySlider.ValueChanged += (_, _) => OnSettingChanged(this, EventArgs.Empty);
        VisionCooldownSecondsTextBox.LostFocus += settingChanged;
        VisionTimeoutSecondsTextBox.LostFocus += settingChanged;
        ScreenshotWidthTextBox.LostFocus += settingChanged;
        ScreenshotHeightTextBox.LostFocus += settingChanged;
        ObservationContextDepthTextBox.LostFocus += settingChanged;
        CommentTopicLimitTextBox.LostFocus += settingChanged;

        // Vision Advanced - Observation timing
        PollIntervalSecondsTextBox.LostFocus += settingChanged;
        MinimumDwellSecondsTextBox.LostFocus += settingChanged;
        StructureCooldownSecondsTextBox.LostFocus += settingChanged;
        CaptureDelayMillisecondsTextBox.LostFocus += settingChanged;

        // Vision Advanced - Retention
        RecentObservationCountTextBox.LostFocus += settingChanged;
        RecentObservationAgeSecondsTextBox.LostFocus += settingChanged;
        StoredObservationCountTextBox.LostFocus += settingChanged;
        StoredDecisionCountTextBox.LostFocus += settingChanged;

        // Commentary
        CommentThresholdSlider.ValueChanged += (_, _) => OnSettingChanged(this, EventArgs.Empty);
        CommentThresholdTextBox.LostFocus += settingChanged;
        CooldownSecondsTextBox.LostFocus += settingChanged;
        CheckInSecondsTextBox.LostFocus += settingChanged;
        DuplicateWindowSecondsTextBox.LostFocus += settingChanged;
        RecentTypingQuietSecondsTextBox.LostFocus += settingChanged;
        NoveltyWeightTextBox.LostFocus += settingChanged;
        RelevanceWeightTextBox.LostFocus += settingChanged;
        PrivacyWeightTextBox.LostFocus += settingChanged;
        InterruptionWeightTextBox.LostFocus += settingChanged;

        // History context
        RegularHistoryMessageCountTextBox.LostFocus += settingChanged;
        AmbientHistoryMessageCountTextBox.LostFocus += settingChanged;
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings || _loadingObservationSettings) return;
        ApplyAllSettings();
    }

    private void OnSettingChanged(object? sender, EventArgs e)
    {
        if (_loadingSettings || _loadingObservationSettings) return;
        ApplyAllSettings();
    }

    private void ApplyAllSettings()
    {
        try
        {
            if (!TryBuildObservationSettings(out var observationSettings, out _))
            {
                return;
            }

            var currentUiSettings = _uiSettingsStore.Load();
            var currentHistorySettings = currentUiSettings.GetEffectiveChatHistoryContext();
            var historySettings = new ChatHistoryContextSettings(
                ClampInt(
                    RegularHistoryMessageCountTextBox.Text,
                    ChatHistoryContextSettings.MinimumMessageCount,
                    ChatHistoryContextSettings.MaximumMessageCount,
                    currentHistorySettings.RegularMessageCount),
                ClampInt(
                    AmbientHistoryMessageCountTextBox.Text,
                    ChatHistoryContextSettings.MinimumMessageCount,
                    ChatHistoryContextSettings.MaximumMessageCount,
                    currentHistorySettings.AmbientMessageCount));

            var currentElevenLabsSettings = _elevenLabsSettingsStore.Load();
            _elevenLabsSettingsStore.Save(currentElevenLabsSettings with
            {
                ElevenLabsApiKey = ToNullIfWhiteSpace(ElevenLabsApiKeyPasswordBox.Password),
                ElevenLabsAgentId = ToNullIfWhiteSpace(ElevenLabsAgentIdTextBox.Text),
                ElevenLabsVoiceId = ToNullIfWhiteSpace(ElevenLabsVoiceIdTextBox.Text)
            });

            var selectedModel = OpenRouterVisionModelComboBox.SelectedItem as OpenRouterModelInfo;
            var selectedAudioModel = OpenRouterAudioModelComboBox.SelectedItem as OpenRouterModelInfo;
            var currentOpenRouterSettings = _openRouterSettingsStore.Load();
            _openRouterSettingsStore.Save(new OpenRouterSettings(
                ToNullIfWhiteSpace(OpenRouterApiKeyPasswordBox.Password),
                ToNullIfWhiteSpace(selectedModel?.Id) ?? currentOpenRouterSettings.VisionModelId,
                ToNullIfWhiteSpace(selectedAudioModel?.Id) ?? currentOpenRouterSettings.AudioAnalysisModelId,
                OpenRouterRequireZdrCheckBox.IsChecked == true));

            _profileSettingsStore.Save(new ProfileSettings(
                ToNullIfWhiteSpace(UserNameTextBox.Text),
                ToNullIfWhiteSpace(NicknameTextBox.Text)));

            var systemRow = _observationRows.FirstOrDefault(r => r.ExecutablePath == SystemRowExecutablePath);
            var systemAudioEnabled = systemRow?.AllowAudio == true;
            var anyPerAppAudio = _observationRows
                .Any(r => r.ExecutablePath != SystemRowExecutablePath && r.AllowAudio);

            var audioSettings = new AudioContextSettings(
                Enabled: systemAudioEnabled || anyPerAppAudio,
                MicrophoneEnabled: systemAudioEnabled,
                SystemAudioEnabled: systemAudioEnabled,
                AnalysisEnabled: true,
                PersistMicrophoneExcerptCheckBox.IsChecked == true,
                PersistSystemAudioExcerptCheckBox.IsChecked == true,
                ClampInt(AudioContextDepthTextBox.Text, 0, 20, 5),
                ClampInt(TranscriptRetentionSecondsTextBox.Text, 1, 3600, 300),
                ClampInt(StoredAudioObservationCountTextBox.Text, 1, 1000, 100),
                ClampDouble(MinimumAudioConfidenceTextBox.Text, 0, 1, 0.60),
                ClampInt(AudioAnalysisTimeoutSecondsTextBox.Text, 5, 180, 45),
                (int)TranscriptVerbositySlider.Value,
                ClampInt(MaxSegmentDurationSecondsTextBox.Text, 5, 60, 30),
                GetSelectedDeviceId(MicrophoneDeviceComboBox),
                GetSelectedDeviceId(SystemAudioDeviceComboBox),
                _observationRows
                    .Where(row => row.ExecutablePath != SystemRowExecutablePath && row.AllowAudio)
                    .Select(row => new AudioApplicationRule(row.ExecutablePath, row.DisplayName, row.AllowAudio))
                    .ToArray());
            _audioContextSettingsStore.Save(audioSettings);
            _audioCaptureCoordinator.ApplySettings(audioSettings);

            // TODO: Simplify when NAudio handles process loopback natively (PR #1225).
            _audioCaptureCoordinator.ApplyPerAppCaptures(
                audioSettings.AudioApplicationRules,
                audioSettings.SystemAudioEnabled,
                TimeSpan.FromSeconds(audioSettings.MaximumSegmentDurationSeconds));
            _audioObservationStore.ApplyRetentionLimit();

            var uiSettings = currentUiSettings with
            {
                ChatShortcut = _selectedChatShortcut,
                PushToTalkShortcut = _selectedPushToTalkShortcut,
                ChatHistoryContext = historySettings
            };
            _uiSettingsStore.Save(uiSettings);
            _permissionService.Save(observationSettings);
            _observationStore.ApplyRetentionLimit();
            _ambientDecisionStore.ApplyRetentionLimit();
            _observationCoordinator.ApplySettings();

            _applyUiSettings(uiSettings);
        }
        catch
        {
        }
    }

    private void LoadSettings()
    {
        _loadingSettings = true;
        try
        {
            var settings = _elevenLabsSettingsStore.Load();
            ElevenLabsApiKeyPasswordBox.Password = settings.ElevenLabsApiKey ?? string.Empty;
            ElevenLabsAgentIdTextBox.Text = settings.ElevenLabsAgentId ?? string.Empty;
            ElevenLabsVoiceIdTextBox.Text = settings.ElevenLabsVoiceId ?? string.Empty;

            var openRouterSettings = _openRouterSettingsStore.Load();
            OpenRouterApiKeyPasswordBox.Password = openRouterSettings.ApiKey ?? string.Empty;
            OpenRouterRequireZdrCheckBox.IsChecked = openRouterSettings.RequireZeroRetention;

            var profileSettings = _profileSettingsStore.Load();
            UserNameTextBox.Text = profileSettings.UserName ?? string.Empty;
            NicknameTextBox.Text = profileSettings.Nickname ?? string.Empty;

            var audioSettings = _audioContextSettingsStore.Load();
            PersistMicrophoneExcerptCheckBox.IsChecked = audioSettings.PersistMicrophoneTranscriptExcerpt;
            PersistSystemAudioExcerptCheckBox.IsChecked = audioSettings.PersistSystemAudioTranscriptExcerpt;
            AudioContextDepthTextBox.Text = audioSettings.ContextDepth.ToString();
            TranscriptRetentionSecondsTextBox.Text = audioSettings.TranscriptRetentionSeconds.ToString();
            StoredAudioObservationCountTextBox.Text = audioSettings.StoredObservationCount.ToString();
            MinimumAudioConfidenceTextBox.Text = audioSettings.MinimumAnalysisConfidence.ToString("0.00");
            AudioAnalysisTimeoutSecondsTextBox.Text = audioSettings.AnalysisTimeoutSeconds.ToString();
            TranscriptVerbositySlider.Value = audioSettings.TranscriptVerbosityLevel;
            TranscriptVerbosityValueText.Text = audioSettings.TranscriptVerbosityLevel.ToString();
            MaxSegmentDurationSecondsTextBox.Text = audioSettings.MaximumSegmentDurationSeconds.ToString();

            LoadAudioDevices(audioSettings.MicrophoneDeviceId, audioSettings.SystemAudioDeviceId);

            var uiSettings = _uiSettingsStore.Load();
            _selectedChatShortcut = uiSettings.ChatShortcut;
            _selectedPushToTalkShortcut = uiSettings.PushToTalkShortcut;
            var historySettings = uiSettings.GetEffectiveChatHistoryContext();
            RegularHistoryMessageCountTextBox.Text = historySettings.RegularMessageCount.ToString();
            AmbientHistoryMessageCountTextBox.Text = historySettings.AmbientMessageCount.ToString();
            UpdateShortcutButtons();

            LoadObservationSettings();
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private void LoadAudioDevices(string? microphoneDeviceId, string? systemAudioDeviceId)
    {
        var microphones = AudioDeviceHelper.GetMicrophoneDevices();
        MicrophoneDeviceComboBox.ItemsSource = microphones;
        MicrophoneDeviceComboBox.SelectedValuePath = nameof(AudioDeviceInfo.Id);
        MicrophoneDeviceComboBox.DisplayMemberPath = nameof(AudioDeviceInfo.DisplayName);
        MicrophoneDeviceComboBox.SelectedItem = string.IsNullOrEmpty(microphoneDeviceId)
            ? microphones.FirstOrDefault(d => d.IsDefault)
            : microphones.FirstOrDefault(d => d.Id == microphoneDeviceId)
              ?? microphones.FirstOrDefault(d => d.IsDefault);

        var speakers = AudioDeviceHelper.GetSystemAudioDevices();
        SystemAudioDeviceComboBox.ItemsSource = speakers;
        SystemAudioDeviceComboBox.SelectedValuePath = nameof(AudioDeviceInfo.Id);
        SystemAudioDeviceComboBox.DisplayMemberPath = nameof(AudioDeviceInfo.DisplayName);
        SystemAudioDeviceComboBox.SelectedItem = string.IsNullOrEmpty(systemAudioDeviceId)
            ? speakers.FirstOrDefault(d => d.IsDefault)
            : speakers.FirstOrDefault(d => d.Id == systemAudioDeviceId)
              ?? speakers.FirstOrDefault(d => d.IsDefault);
    }

    private static string? GetSelectedDeviceId(System.Windows.Controls.ComboBox comboBox)
    {
        if (comboBox.SelectedItem is AudioDeviceInfo device)
        {
            return device.IsDefault ? null : device.Id;
        }

        return null;
    }

    private static int ParseInt(System.Windows.Controls.TextBox textBox, int fallback) =>
        int.TryParse(textBox.Text, out var value) ? value : fallback;

    private static double ParseDouble(string text, double fallback) =>
        double.TryParse(text, out var value) ? value : fallback;

    private static int ClampInt(string text, int min, int max, int fallback)
    {
        if (int.TryParse(text, out var value))
        {
            return Math.Clamp(value, min, max);
        }

        return fallback;
    }

    private static double ClampDouble(string text, double min, double max, double fallback)
    {
        return double.TryParse(text, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;
    }

    private static string? ToNullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
