using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
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
    private readonly CharacterErrorMessageStore _errorMessageStore;
    private readonly Func<UiSettings, PetError?> _applyUiSettings;
    private readonly Func<PetError?> _getHotkeyWarning;
    private readonly IObservationPermissionService _permissionService;
    private readonly ObservationStore _observationStore;
    private readonly AmbientDecisionStore _ambientDecisionStore;
    private readonly IDesktopObservationCoordinator _observationCoordinator;
    private readonly ObservableCollection<ApplicationRuleRow> _observationRows = [];
    private readonly ObservableCollection<OpenRouterModelInfo> _visionModels = [];
    private KeyboardShortcut _selectedChatShortcut = KeyboardShortcut.DefaultChatShortcut;
    private bool _isRecordingShortcut;
    private bool _loadingObservationSettings;
    private bool _syncingCommentThreshold;

    public SettingsWindow(
        ElevenLabsSettingsStore elevenLabsSettingsStore,
        ElevenLabsPronunciationService pronunciationService,
        OpenRouterSettingsStore openRouterSettingsStore,
        OpenRouterModelsService openRouterModelsService,
        CreditInfoService creditInfoService,
        UiSettingsStore uiSettingsStore,
        ProfileSettingsStore profileSettingsStore,
        CharacterErrorMessageStore errorMessageStore,
        Func<UiSettings, PetError?> applyUiSettings,
        Func<PetError?> getHotkeyWarning,
        IObservationPermissionService permissionService,
        ObservationStore observationStore,
        AmbientDecisionStore ambientDecisionStore,
        IDesktopObservationCoordinator observationCoordinator)
    {
        _elevenLabsSettingsStore = elevenLabsSettingsStore;
        _pronunciationService = pronunciationService;
        _openRouterSettingsStore = openRouterSettingsStore;
        _openRouterModelsService = openRouterModelsService;
        _creditInfoService = creditInfoService;
        _uiSettingsStore = uiSettingsStore;
        _profileSettingsStore = profileSettingsStore;
        _errorMessageStore = errorMessageStore;
        _applyUiSettings = applyUiSettings;
        _getHotkeyWarning = getHotkeyWarning;
        _permissionService = permissionService;
        _observationStore = observationStore;
        _ambientDecisionStore = ambientDecisionStore;
        _observationCoordinator = observationCoordinator;

        InitializeComponent();
        ApplicationsGrid.ItemsSource = _observationRows;
        OpenRouterVisionModelComboBox.ItemsSource = _visionModels;
        LoadSettings();
        _ = LoadVisionModelsAsync();
        _ = LoadCreditsAsync();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!TryBuildObservationSettings(out var observationSettings, out var validationMessage))
            {
                StatusTextBlock.Text = validationMessage;
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
            _openRouterSettingsStore.Save(new OpenRouterSettings(
                ToNullIfWhiteSpace(OpenRouterApiKeyPasswordBox.Password),
                ToNullIfWhiteSpace(selectedModel?.Id),
                OpenRouterRequireZdrCheckBox.IsChecked == true));

            _profileSettingsStore.Save(new ProfileSettings(
                ToNullIfWhiteSpace(UserNameTextBox.Text),
                ToNullIfWhiteSpace(NicknameTextBox.Text)));

            var uiSettings = currentUiSettings with
            {
                ChatShortcut = _selectedChatShortcut,
                ChatHistoryContext = historySettings
            };
            _uiSettingsStore.Save(uiSettings);
            RegularHistoryMessageCountTextBox.Text = historySettings.RegularMessageCount.ToString();
            AmbientHistoryMessageCountTextBox.Text = historySettings.AmbientMessageCount.ToString();

            _permissionService.Save(observationSettings);
            _observationStore.ApplyRetentionLimit();
            _ambientDecisionStore.ApplyRetentionLimit();
            _observationCoordinator.ApplySettings();
            LoadObservationSettings();

            var hotkeyWarning = _applyUiSettings(uiSettings);
            StatusTextBlock.Text = hotkeyWarning is null
                ? "Saved."
                : $"Saved, but {_errorMessageStore.GetMessage(hotkeyWarning.Code)}";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Save failed: {ex.Message}";
        }
    }

    private void LoadSettings()
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

        var uiSettings = _uiSettingsStore.Load();
        _selectedChatShortcut = uiSettings.ChatShortcut;
        var historySettings = uiSettings.GetEffectiveChatHistoryContext();
        RegularHistoryMessageCountTextBox.Text = historySettings.RegularMessageCount.ToString();
        AmbientHistoryMessageCountTextBox.Text = historySettings.AmbientMessageCount.ToString();
        UpdateShortcutButton();

        var hotkeyWarning = _getHotkeyWarning();
        if (hotkeyWarning is not null)
        {
            StatusTextBlock.Text = _errorMessageStore.GetMessage(hotkeyWarning.Code);
        }

        LoadObservationSettings();
    }

    private void LoadObservationSettings()
    {
        var settings = _permissionService.Current;
        _loadingObservationSettings = true;
        ObservationEnabledCheckBox.IsChecked = settings.ObservationEnabled;
        AmbientCommentsEnabledCheckBox.IsChecked = settings.AmbientCommentsEnabled;
        SetCommentaryPreset(ObservationSettingLimits.MatchPreset(
            settings.CooldownMinutes,
            settings.CheckInMinutes,
            settings.DuplicateWindowMinutes));
        CooldownMinutesTextBox.Text = settings.CooldownMinutes.ToString();
        CheckInMinutesTextBox.Text = settings.CheckInMinutes.ToString();
        DuplicateWindowMinutesTextBox.Text = settings.DuplicateWindowMinutes.ToString();
        RecentTypingQuietSecondsTextBox.Text = settings.RecentTypingQuietSeconds.ToString();
        NoveltyWeightTextBox.Text = settings.NoveltyWeightPercent.ToString("0.##");
        RelevanceWeightTextBox.Text = settings.RelevanceWeightPercent.ToString("0.##");
        PrivacyWeightTextBox.Text = settings.PrivacySafetyWeightPercent.ToString("0.##");
        InterruptionWeightTextBox.Text = settings.LowInterruptionCostWeightPercent.ToString("0.##");
        PollIntervalSecondsTextBox.Text = settings.PollIntervalSeconds.ToString();
        MinimumDwellSecondsTextBox.Text = settings.MinimumDwellTimeSeconds.ToString();
        StructureCooldownSecondsTextBox.Text = settings.StructureInspectionCooldownSeconds.ToString();
        CaptureDelayMillisecondsTextBox.Text = settings.ScreenshotCaptureDelayMilliseconds.ToString();
        VisionCooldownSecondsTextBox.Text = settings.VisionAnalysisCooldownSeconds.ToString();
        VisionTimeoutSecondsTextBox.Text = settings.VisionRequestTimeoutSeconds.ToString();
        ScreenshotWidthTextBox.Text = settings.MaximumScreenshotWidth.ToString();
        ScreenshotHeightTextBox.Text = settings.MaximumScreenshotHeight.ToString();
        ObservationContextDepthTextBox.Text = settings.ObservationContextDepth.ToString();
        CommentTopicLimitTextBox.Text = settings.CommentTopicLimit.ToString();
        RecentObservationCountTextBox.Text = settings.RecentObservationCount.ToString();
        RecentObservationAgeTextBox.Text = settings.RecentObservationAgeMinutes.ToString();
        StoredObservationCountTextBox.Text = settings.StoredObservationCount.ToString();
        StoredDecisionCountTextBox.Text = settings.StoredAmbientDecisionCount.ToString();
        CommentThresholdSlider.Value = settings.CommentThresholdPercent;
        CommentThresholdTextBox.Text = settings.CommentThresholdPercent.ToString();

        switch (settings.ScanQuality)
        {
            case ScanQuality.Brief:
                ScanQualityBriefRadioButton.IsChecked = true;
                break;
            case ScanQuality.Narrative:
                ScanQualityNarrativeRadioButton.IsChecked = true;
                break;
            default:
                ScanQualityDetailedRadioButton.IsChecked = true;
                break;
        }
        _loadingObservationSettings = false;
        UpdateCommentaryPresetState(populateValues: false);

        var rows = settings.ApplicationRules
            .Select(ApplicationRuleRow.FromRule)
            .ToDictionary(row => row.ExecutablePath, StringComparer.OrdinalIgnoreCase);

        foreach (var application in ListRunningApplications())
        {
            rows.TryAdd(application.ExecutablePath, application);
        }

        _observationRows.Clear();
        foreach (var row in rows.Values.OrderBy(row => row.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            _observationRows.Add(row);
        }
    }

    private void OnManagePronunciationsClicked(object sender, RoutedEventArgs e)
    {
        var apiKey = ToNullIfWhiteSpace(ElevenLabsApiKeyPasswordBox.Password);
        if (apiKey is null)
        {
            StatusTextBlock.Text = "Enter an ElevenLabs API key first.";
            return;
        }

        var window = new PronunciationWindow(
            _elevenLabsSettingsStore,
            _pronunciationService,
            apiKey)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private bool TryBuildObservationSettings(
        out ObservationSettings settings,
        out string validationMessage)
    {
        var current = _permissionService.Current;
        var rules = _observationRows
            .Where(row => row.HasDecision)
            .Select(row => row.ToRule())
            .ToArray();

        var weights = new[]
        {
            ParseDouble(NoveltyWeightTextBox.Text, current.NoveltyWeightPercent),
            ParseDouble(RelevanceWeightTextBox.Text, current.RelevanceWeightPercent),
            ParseDouble(PrivacyWeightTextBox.Text, current.PrivacySafetyWeightPercent),
            ParseDouble(InterruptionWeightTextBox.Text, current.LowInterruptionCostWeightPercent)
        };
        if (Math.Abs(weights.Sum() - 100d) > 0.001)
        {
            settings = current;
            validationMessage = "Interest weights must total exactly 100%.";
            return false;
        }

        settings = ObservationSettingsStore.Normalize(current with
        {
            ObservationEnabled = ObservationEnabledCheckBox.IsChecked == true,
            AmbientCommentsEnabled = AmbientCommentsEnabledCheckBox.IsChecked == true,
            CooldownMinutes = ParseInt(CooldownMinutesTextBox, current.CooldownMinutes),
            DuplicateWindowMinutes = ParseInt(DuplicateWindowMinutesTextBox, current.DuplicateWindowMinutes),
            CheckInMinutes = ParseInt(CheckInMinutesTextBox, current.CheckInMinutes),
            CommentThresholdPercent = ParseInt(CommentThresholdTextBox, current.CommentThresholdPercent),
            NoveltyWeightPercent = weights[0],
            RelevanceWeightPercent = weights[1],
            PrivacySafetyWeightPercent = weights[2],
            LowInterruptionCostWeightPercent = weights[3],
            ScanQuality = ScanQualityNarrativeRadioButton.IsChecked == true
                ? ScanQuality.Narrative
                : ScanQualityBriefRadioButton.IsChecked == true
                    ? ScanQuality.Brief
                    : ScanQuality.Detailed,
            RecentTypingQuietSeconds = ParseInt(RecentTypingQuietSecondsTextBox, current.RecentTypingQuietSeconds),
            PollIntervalSeconds = ParseInt(PollIntervalSecondsTextBox, current.PollIntervalSeconds),
            MinimumDwellTimeSeconds = ParseInt(MinimumDwellSecondsTextBox, current.MinimumDwellTimeSeconds),
            StructureInspectionCooldownSeconds = ParseInt(StructureCooldownSecondsTextBox, current.StructureInspectionCooldownSeconds),
            ScreenshotCaptureDelayMilliseconds = ParseInt(CaptureDelayMillisecondsTextBox, current.ScreenshotCaptureDelayMilliseconds),
            VisionAnalysisCooldownSeconds = ParseInt(VisionCooldownSecondsTextBox, current.VisionAnalysisCooldownSeconds),
            VisionRequestTimeoutSeconds = ParseInt(VisionTimeoutSecondsTextBox, current.VisionRequestTimeoutSeconds),
            MaximumScreenshotWidth = ParseInt(ScreenshotWidthTextBox, current.MaximumScreenshotWidth),
            MaximumScreenshotHeight = ParseInt(ScreenshotHeightTextBox, current.MaximumScreenshotHeight),
            ObservationContextDepth = ParseInt(ObservationContextDepthTextBox, current.ObservationContextDepth),
            CommentTopicLimit = ParseInt(CommentTopicLimitTextBox, current.CommentTopicLimit),
            RecentObservationCount = ParseInt(RecentObservationCountTextBox, current.RecentObservationCount),
            RecentObservationAgeMinutes = ParseInt(RecentObservationAgeTextBox, current.RecentObservationAgeMinutes),
            StoredObservationCount = ParseInt(StoredObservationCountTextBox, current.StoredObservationCount),
            StoredAmbientDecisionCount = ParseInt(StoredDecisionCountTextBox, current.StoredAmbientDecisionCount),
            ApplicationRules = rules
        });
        validationMessage = string.Empty;
        return true;
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

    private void OnCommentaryLevelChanged(object sender, RoutedEventArgs e)
    {
        if (CommentaryLegendTextBlock is null) return;
        UpdateCommentaryPresetState(populateValues: !_loadingObservationSettings);
    }

    private void UpdateCommentaryPresetState(bool populateValues)
    {
        var preset = GetSelectedCommentaryPreset();
        var custom = preset == CommentaryPreset.Custom;
        CooldownMinutesTextBox.IsEnabled = custom;
        CheckInMinutesTextBox.IsEnabled = custom;
        DuplicateWindowMinutesTextBox.IsEnabled = custom;
        if (!custom && populateValues)
        {
            var timing = ObservationSettingLimits.GetPreset(preset);
            CooldownMinutesTextBox.Text = timing.CooldownMinutes.ToString();
            CheckInMinutesTextBox.Text = timing.CheckInMinutes.ToString();
            DuplicateWindowMinutesTextBox.Text = timing.DuplicateWindowMinutes.ToString();
        }

        CommentaryLegendTextBlock.Text = custom
            ? "Use the exact timing values in Advanced."
            : $"Comments every ~{ObservationSettingLimits.GetPreset(preset).CooldownMinutes} min; "
                + $"checks every {ObservationSettingLimits.GetPreset(preset).CheckInMinutes} min; "
                + $"duplicates suppressed for {ObservationSettingLimits.GetPreset(preset).DuplicateWindowMinutes} min.";
    }

    private CommentaryPreset GetSelectedCommentaryPreset() =>
        CommentaryQuietRadioButton.IsChecked == true ? CommentaryPreset.Quiet :
        CommentaryTalkativeRadioButton.IsChecked == true ? CommentaryPreset.Talkative :
        CommentaryCustomRadioButton.IsChecked == true ? CommentaryPreset.Custom :
        CommentaryPreset.Balanced;

    private void SetCommentaryPreset(CommentaryPreset preset)
    {
        CommentaryQuietRadioButton.IsChecked = preset == CommentaryPreset.Quiet;
        CommentaryBalancedRadioButton.IsChecked = preset == CommentaryPreset.Balanced;
        CommentaryTalkativeRadioButton.IsChecked = preset == CommentaryPreset.Talkative;
        CommentaryCustomRadioButton.IsChecked = preset == CommentaryPreset.Custom;
    }

    private void OnCommentThresholdSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingCommentThreshold || CommentThresholdTextBox is null) return;
        _syncingCommentThreshold = true;
        CommentThresholdTextBox.Text = ((int)Math.Round(e.NewValue)).ToString();
        _syncingCommentThreshold = false;
    }

    private void OnCommentThresholdTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_syncingCommentThreshold || CommentThresholdSlider is null) return;
        if (int.TryParse(CommentThresholdTextBox.Text, out var value))
        {
            _syncingCommentThreshold = true;
            CommentThresholdSlider.Value = Math.Clamp(value, 0, 100);
            _syncingCommentThreshold = false;
        }
    }

    private void OnNumericPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(character => !char.IsDigit(character) && character != '.');
    }

    private void OnScanQualityChanged(object sender, RoutedEventArgs e)
    {
        if (ScanQualityLegendTextBlock is null) return;
        ScanQualityLegendTextBlock.Text = ScanQualityBriefRadioButton.IsChecked == true
            ? "Quick summaries with minimal token usage."
            : ScanQualityNarrativeRadioButton.IsChecked == true
                ? "Rich scene descriptions that give Pebble more to comment on."
                : "Balanced detail with activity context and notable elements.";
    }

    private static IEnumerable<ApplicationRuleRow> ListRunningApplications()
    {
        var applications = new List<ApplicationRuleRow>();
        var currentProcessId = Environment.ProcessId;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == currentProcessId || process.MainWindowHandle == nint.Zero)
                    {
                        continue;
                    }

                    var path = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    applications.Add(new ApplicationRuleRow
                    {
                        ExecutablePath = ObservationApplicationIdentity.NormalizePath(path),
                        DisplayName = string.IsNullOrWhiteSpace(process.MainWindowTitle)
                            ? Path.GetFileNameWithoutExtension(path)
                            : process.ProcessName
                    });
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                }
            }
        }

        return applications;
    }

    private static string? ToNullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async Task LoadVisionModelsAsync()
    {
        var openRouterSettings = _openRouterSettingsStore.Load();
        if (string.IsNullOrWhiteSpace(openRouterSettings.ApiKey))
        {
            OpenRouterModelCapabilitiesText.Text = "Enter an API key to load available vision models.";
            return;
        }

        OpenRouterModelCapabilitiesText.Text = "Loading vision models...";

        try
        {
            var models = await _openRouterModelsService.GetVisionModelsAsync(CancellationToken.None);
            _visionModels.Clear();
            foreach (var model in models)
            {
                _visionModels.Add(model);
            }

            if (_visionModels.Count == 0)
            {
                OpenRouterModelCapabilitiesText.Text = "No vision-capable models found. Check your API key.";
                return;
            }

            var selectedModel = _visionModels.FirstOrDefault(m =>
                string.Equals(m.Id, openRouterSettings.VisionModelId, StringComparison.OrdinalIgnoreCase));

            if (selectedModel is not null)
            {
                OpenRouterVisionModelComboBox.SelectedItem = selectedModel;
                UpdateModelCapabilities(selectedModel);
            }
            else if (_visionModels.Count > 0)
            {
                OpenRouterVisionModelComboBox.SelectedIndex = 0;
                UpdateModelCapabilities(_visionModels[0]);
            }
        }
        catch (Exception)
        {
            OpenRouterModelCapabilitiesText.Text = "Failed to load vision models. Check your API key and connection.";
        }
    }

    private void UpdateModelCapabilities(OpenRouterModelInfo model)
    {
        var capabilities = new List<string>();
        capabilities.Add("Image input");
        if (model.SupportsStructuredOutput)
        {
            capabilities.Add("Structured output");
        }

        OpenRouterModelCapabilitiesText.Text = $"Capabilities: {string.Join(", ", capabilities)}";
    }

    private async Task LoadCreditsAsync()
    {
        var elevenLabsTask = _creditInfoService.GetElevenLabsCreditsAsync(CancellationToken.None);
        var openRouterTask = _creditInfoService.GetOpenRouterCreditsAsync(CancellationToken.None);

        await Task.WhenAll(elevenLabsTask, openRouterTask);

        var elevenLabs = elevenLabsTask.Result;
        if (elevenLabs is not null)
        {
            ElevenLabsCreditText.Text = $"{elevenLabs.Tier} — {elevenLabs.CharacterCount:N0} / {elevenLabs.CharacterLimit:N0} credits";
        }

        var openRouter = openRouterTask.Result;
        if (openRouter is not null)
        {
            if (openRouter.LimitRemaining.HasValue)
            {
                OpenRouterCreditText.Text = $"${openRouter.LimitRemaining:F2} remaining";
            }
            else
            {
                OpenRouterCreditText.Text = $"${openRouter.Usage:F2} used";
            }
        }
    }

    private void OnRecordShortcutClicked(object sender, RoutedEventArgs e)
    {
        _isRecordingShortcut = true;
        ChatShortcutButton.Content = "Press shortcut...";
        StatusTextBlock.Text = "Press a key with Ctrl, Alt, Shift, or Win. Esc cancels.";
        ChatShortcutButton.Focus();
    }

    private void OnResetShortcutClicked(object sender, RoutedEventArgs e)
    {
        _isRecordingShortcut = false;
        _selectedChatShortcut = KeyboardShortcut.DefaultChatShortcut;
        UpdateShortcutButton();
        StatusTextBlock.Text = "Shortcut reset. Save to apply.";
    }

    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_isRecordingShortcut)
        {
            return;
        }

        e.Handled = true;

        var key = GetRealKey(e);
        if (key == Key.Escape)
        {
            _isRecordingShortcut = false;
            UpdateShortcutButton();
            StatusTextBlock.Text = "Shortcut recording cancelled.";
            return;
        }

        if (KeyboardShortcut.IsModifierKey(key))
        {
            StatusTextBlock.Text = "Press a non-modifier key too.";
            return;
        }

        var shortcut = KeyboardShortcut.FromWpfInput(key, Keyboard.Modifiers);
        if (!shortcut.IsValid())
        {
            StatusTextBlock.Text = "Shortcut must include Ctrl, Alt, Shift, or Win.";
            return;
        }

        _selectedChatShortcut = shortcut;
        _isRecordingShortcut = false;
        UpdateShortcutButton();
        StatusTextBlock.Text = "Shortcut captured. Save to apply.";
    }

    private void UpdateShortcutButton()
    {
        ChatShortcutButton.Content = _selectedChatShortcut.DisplayText;
    }

    private static Key GetRealKey(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.System)
        {
            return e.SystemKey;
        }

        return e.Key == Key.ImeProcessed
            ? e.ImeProcessedKey
            : e.Key;
    }
}

public sealed class ApplicationRuleRow : INotifyPropertyChanged
{
    private bool _isDenied;
    private bool _allowMetadata;
    private bool _allowStructure;
    private bool _allowVisual;

    public required string ExecutablePath { get; init; }

    public required string DisplayName { get; init; }

    public bool IsDenied
    {
        get => _isDenied;
        set
        {
            if (SetField(ref _isDenied, value) && value)
            {
                AllowMetadata = false;
                AllowStructure = false;
                AllowVisual = false;
            }
        }
    }

    public bool AllowMetadata
    {
        get => _allowMetadata;
        set
        {
            if (SetField(ref _allowMetadata, value) && value)
            {
                IsDenied = false;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllowMetadataAndStructure)));
        }
    }

    public bool AllowStructure
    {
        get => _allowStructure;
        set
        {
            if (SetField(ref _allowStructure, value) && value)
            {
                IsDenied = false;
                AllowMetadata = true;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllowMetadataAndStructure)));
        }
    }

    public bool AllowMetadataAndStructure
    {
        get => AllowMetadata && AllowStructure;
        set
        {
            AllowMetadata = value;
            AllowStructure = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllowMetadataAndStructure)));
        }
    }

    public bool AllowVisual
    {
        get => _allowVisual;
        set
        {
            if (SetField(ref _allowVisual, value) && value)
            {
                IsDenied = false;
                AllowMetadataAndStructure = true;
            }
        }
    }

    public bool HasDecision => IsDenied || AllowMetadata || AllowStructure || AllowVisual;

    public event PropertyChangedEventHandler? PropertyChanged;

    public static ApplicationRuleRow FromRule(ApplicationObservationRule rule)
    {
        var allowCombinedMetadata = rule.AllowMetadata && rule.AllowStructure;
        return new ApplicationRuleRow
        {
            ExecutablePath = rule.ExecutablePath,
            DisplayName = rule.DisplayName,
            _isDenied = rule.IsDenied,
            _allowMetadata = allowCombinedMetadata,
            _allowStructure = allowCombinedMetadata,
            _allowVisual = rule.AllowVisual
        };
    }

    public ApplicationObservationRule ToRule()
    {
        return new ApplicationObservationRule(
            ExecutablePath,
            DisplayName,
            IsDenied,
            AllowMetadata,
            AllowStructure,
            AllowVisual);
    }

    private bool SetField(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDecision)));
        return true;
    }
}
