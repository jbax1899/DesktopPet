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
    private readonly OpenRouterSettingsStore _openRouterSettingsStore;
    private readonly OpenRouterModelsService _openRouterModelsService;
    private readonly UiSettingsStore _uiSettingsStore;
    private readonly ProfileSettingsStore _profileSettingsStore;
    private readonly CharacterErrorMessageStore _errorMessageStore;
    private readonly Func<UiSettings, PetError?> _applyUiSettings;
    private readonly Func<PetError?> _getHotkeyWarning;
    private readonly IObservationPermissionService _permissionService;
    private readonly Func<Task> _testVisionAsync;
    private readonly ObservableCollection<ApplicationRuleRow> _observationRows = [];
    private readonly ObservableCollection<OpenRouterModelInfo> _visionModels = [];
    private KeyboardShortcut _selectedChatShortcut = KeyboardShortcut.DefaultChatShortcut;
    private bool _isRecordingShortcut;

    public SettingsWindow(
        ElevenLabsSettingsStore elevenLabsSettingsStore,
        OpenRouterSettingsStore openRouterSettingsStore,
        OpenRouterModelsService openRouterModelsService,
        UiSettingsStore uiSettingsStore,
        ProfileSettingsStore profileSettingsStore,
        CharacterErrorMessageStore errorMessageStore,
        Func<UiSettings, PetError?> applyUiSettings,
        Func<PetError?> getHotkeyWarning,
        IObservationPermissionService permissionService,
        Func<Task>? testVisionAsync = null)
    {
        _elevenLabsSettingsStore = elevenLabsSettingsStore;
        _openRouterSettingsStore = openRouterSettingsStore;
        _openRouterModelsService = openRouterModelsService;
        _uiSettingsStore = uiSettingsStore;
        _profileSettingsStore = profileSettingsStore;
        _errorMessageStore = errorMessageStore;
        _applyUiSettings = applyUiSettings;
        _getHotkeyWarning = getHotkeyWarning;
        _permissionService = permissionService;
        _testVisionAsync = testVisionAsync ?? (() => Task.CompletedTask);

        InitializeComponent();
        ApplicationsGrid.ItemsSource = _observationRows;
        OpenRouterVisionModelComboBox.ItemsSource = _visionModels;
        LoadSettings();
        _ = LoadVisionModelsAsync();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _elevenLabsSettingsStore.Save(new ElevenLabsSettings(
                ToNullIfWhiteSpace(ElevenLabsApiKeyPasswordBox.Password),
                ToNullIfWhiteSpace(ElevenLabsAgentIdTextBox.Text),
                ToNullIfWhiteSpace(ElevenLabsVoiceIdTextBox.Text)));

            var selectedModel = OpenRouterVisionModelComboBox.SelectedItem as OpenRouterModelInfo;
            _openRouterSettingsStore.Save(new OpenRouterSettings(
                ToNullIfWhiteSpace(OpenRouterApiKeyPasswordBox.Password),
                ToNullIfWhiteSpace(selectedModel?.Id),
                OpenRouterRequireZdrCheckBox.IsChecked == true));

            _profileSettingsStore.Save(new ProfileSettings(
                ToNullIfWhiteSpace(UserNameTextBox.Text),
                ToNullIfWhiteSpace(NicknameTextBox.Text)));

            var currentUiSettings = _uiSettingsStore.Load();
            var uiSettings = currentUiSettings with
            {
                ChatShortcut = _selectedChatShortcut
            };
            _uiSettingsStore.Save(uiSettings);

            SaveObservationSettings();

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

        _selectedChatShortcut = _uiSettingsStore.Load().ChatShortcut;
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
        ObservationEnabledCheckBox.IsChecked = settings.ObservationEnabled;
        AmbientCommentsEnabledCheckBox.IsChecked = settings.AmbientCommentsEnabled;

        switch (settings.CommentaryLevel)
        {
            case CommentaryLevel.Quiet:
                CommentaryQuietRadioButton.IsChecked = true;
                break;
            case CommentaryLevel.Talkative:
                CommentaryTalkativeRadioButton.IsChecked = true;
                break;
            default:
                CommentaryBalancedRadioButton.IsChecked = true;
                break;
        }

        switch (settings.VisionSensitivity)
        {
            case VisionSensitivity.Low:
                VisionLowRadioButton.IsChecked = true;
                break;
            case VisionSensitivity.High:
                VisionHighRadioButton.IsChecked = true;
                break;
            default:
                VisionMediumRadioButton.IsChecked = true;
                break;
        }

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

    private void SaveObservationSettings()
    {
        var current = _permissionService.Current;
        var rules = _observationRows
            .Where(row => row.HasDecision)
            .Select(row => row.ToRule())
            .ToArray();

        _permissionService.Save(current with
        {
            ObservationEnabled = ObservationEnabledCheckBox.IsChecked == true,
            AmbientCommentsEnabled = AmbientCommentsEnabledCheckBox.IsChecked == true,
            CommentaryLevel = CommentaryTalkativeRadioButton.IsChecked == true
                ? CommentaryLevel.Talkative
                : CommentaryQuietRadioButton.IsChecked == true
                    ? CommentaryLevel.Quiet
                    : CommentaryLevel.Balanced,
            VisionSensitivity = VisionHighRadioButton.IsChecked == true
                ? VisionSensitivity.High
                : VisionLowRadioButton.IsChecked == true
                    ? VisionSensitivity.Low
                    : VisionSensitivity.Medium,
            ScanQuality = ScanQualityNarrativeRadioButton.IsChecked == true
                ? ScanQuality.Narrative
                : ScanQualityBriefRadioButton.IsChecked == true
                    ? ScanQuality.Brief
                    : ScanQuality.Detailed,
            MinimumDwellTimeSeconds = current.MinimumDwellTimeSeconds,
            VisionAnalysisCooldownSeconds = current.VisionAnalysisCooldownSeconds,
            ApplicationRules = rules
        });
    }

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
        CommentaryLegendTextBlock.Text = CommentaryQuietRadioButton.IsChecked == true
            ? "Rare comments, long silence between remarks."
            : CommentaryTalkativeRadioButton.IsChecked == true
                ? "Frequent comments, notices small changes quickly."
                : "Moderate cadence, balanced between silence and speech.";
    }

    private void OnVisionSensitivityChanged(object sender, RoutedEventArgs e)
    {
        if (VisionSensitivityLegendTextBlock is null) return;
        VisionSensitivityLegendTextBlock.Text = VisionLowRadioButton.IsChecked == true
            ? "Only highly interesting changes trigger analysis."
            : VisionHighRadioButton.IsChecked == true
                ? "More things trigger analysis, including subtle changes."
                : "Balanced interest threshold for most situations.";
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

    private async void OnTestVisionClicked(object sender, RoutedEventArgs e)
    {
        TestVisionStatusText.Text = "Testing...";
        TestVisionButton.IsEnabled = false;

        try
        {
            await _testVisionAsync();
            TestVisionStatusText.Text = "Vision test passed.";
        }
        catch (Exception ex)
        {
            TestVisionStatusText.Text = $"Test failed: {ex.Message}";
        }
        finally
        {
            TestVisionButton.IsEnabled = true;
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
