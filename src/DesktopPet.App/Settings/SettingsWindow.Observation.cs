using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using DesktopPet.App.Cloud;
using DesktopPet.App.Observation;

namespace DesktopPet.App.Settings;

public partial class SettingsWindow
{
    private const string SystemRowExecutablePath = "";

    private void LoadObservationSettings()
    {
        var settings = _permissionService.Current;
        _loadingObservationSettings = true;
        CaptureScreenshotOnChatSendCheckBox.IsChecked = settings.CaptureScreenshotOnChatSend;
        SetCommentaryPreset(ObservationSettingLimits.MatchPreset(
            settings.CooldownSeconds,
            settings.CheckInSeconds,
            settings.DuplicateWindowSeconds));
        CooldownSecondsTextBox.Text = settings.CooldownSeconds.ToString();
        CheckInSecondsTextBox.Text = settings.CheckInSeconds.ToString();
        DuplicateWindowSecondsTextBox.Text = settings.DuplicateWindowSeconds.ToString();
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
        RecentObservationAgeSecondsTextBox.Text = settings.RecentObservationAgeSeconds.ToString();
        StoredObservationCountTextBox.Text = settings.StoredObservationCount.ToString();
        StoredDecisionCountTextBox.Text = settings.StoredAmbientDecisionCount.ToString();
        CommentThresholdSlider.Value = settings.CommentThresholdPercent;
        CommentThresholdTextBox.Text = settings.CommentThresholdPercent.ToString();

        VisionDetailSlider.Value = settings.VisionDetailLevel;
        VisionDetailValueText.Text = settings.VisionDetailLevel.ToString();
        UpdateVisionDetailLegend(settings.VisionDetailLevel);

        VisionVerbositySlider.Value = settings.VisionVerbosityLevel;
        VisionVerbosityValueText.Text = settings.VisionVerbosityLevel.ToString();
        UpdateVisionVerbosityLegend(settings.VisionVerbosityLevel);

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

        // System row — controls system-wide audio capture.
        var audioSettings = _audioContextSettingsStore.Load();
        var systemRow = new ApplicationRuleRow
        {
            ExecutablePath = SystemRowExecutablePath,
            DisplayName = "System"
        };
        systemRow.AllowAudio = audioSettings.SystemAudioEnabled;
        _observationRows.Add(systemRow);

        foreach (var row in rows.Values.OrderBy(row => row.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            _observationRows.Add(row);
        }

        UpdatePerAppColumnEnabledState();
    }

    private bool TryBuildObservationSettings(
        out ObservationSettings settings,
        out string validationMessage)
    {
        var current = _permissionService.Current;

        // Skip the System row (empty ExecutablePath) when building rules.
        var rules = _observationRows
            .Where(row => row.HasDecision && row.ExecutablePath != SystemRowExecutablePath)
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

        var commentaryPreset = GetSelectedCommentaryPreset();

        // Derive ObservationEnabled: true if any non-System row allows metadata or vision.
        var observationEnabled = _observationRows
            .Any(r => r.ExecutablePath != SystemRowExecutablePath
                      && (r.AllowMetadata || r.AllowVisual));

        settings = ObservationSettingsStore.Normalize(current with
        {
            ObservationEnabled = observationEnabled,
            AmbientCommentsEnabled = commentaryPreset != CommentaryPreset.Off,
            CaptureScreenshotOnChatSend = CaptureScreenshotOnChatSendCheckBox.IsChecked == true,
            CooldownSeconds = ParseInt(CooldownSecondsTextBox, current.CooldownSeconds),
            DuplicateWindowSeconds = ParseInt(DuplicateWindowSecondsTextBox, current.DuplicateWindowSeconds),
            CheckInSeconds = ParseInt(CheckInSecondsTextBox, current.CheckInSeconds),
            CommentThresholdPercent = ParseInt(CommentThresholdTextBox, current.CommentThresholdPercent),
            NoveltyWeightPercent = weights[0],
            RelevanceWeightPercent = weights[1],
            PrivacySafetyWeightPercent = weights[2],
            LowInterruptionCostWeightPercent = weights[3],
            VisionDetailLevel = (int)VisionDetailSlider.Value,
            VisionVerbosityLevel = (int)VisionVerbositySlider.Value,
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
            RecentObservationAgeSeconds = ParseInt(RecentObservationAgeSecondsTextBox, current.RecentObservationAgeSeconds),
            StoredObservationCount = ParseInt(StoredObservationCountTextBox, current.StoredObservationCount),
            StoredAmbientDecisionCount = ParseInt(StoredDecisionCountTextBox, current.StoredAmbientDecisionCount),
            ApplicationRules = rules
        });
        validationMessage = string.Empty;
        return true;
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
        var off = preset == CommentaryPreset.Off;
        CooldownSecondsTextBox.IsEnabled = custom;
        CheckInSecondsTextBox.IsEnabled = custom;
        DuplicateWindowSecondsTextBox.IsEnabled = custom;
        if (!custom && populateValues)
        {
            var timing = ObservationSettingLimits.GetPreset(preset);
            CooldownSecondsTextBox.Text = timing.CooldownSeconds.ToString();
            CheckInSecondsTextBox.Text = timing.CheckInSeconds.ToString();
            DuplicateWindowSecondsTextBox.Text = timing.DuplicateWindowSeconds.ToString();
        }

        CommentaryLegendTextBlock.Text = off
            ? "Ambient comments are disabled."
            : custom
                ? string.Empty
                : $"Comments every ~{ObservationSettingLimits.GetPreset(preset).CooldownSeconds} sec; "
                    + $"checks every {ObservationSettingLimits.GetPreset(preset).CheckInSeconds} sec; "
                    + $"duplicates suppressed for {ObservationSettingLimits.GetPreset(preset).DuplicateWindowSeconds} sec.";
    }

    private CommentaryPreset GetSelectedCommentaryPreset() =>
        CommentaryOffRadioButton.IsChecked == true ? CommentaryPreset.Off :
        CommentaryQuietRadioButton.IsChecked == true ? CommentaryPreset.Quiet :
        CommentaryTalkativeRadioButton.IsChecked == true ? CommentaryPreset.Talkative :
        CommentaryCustomRadioButton.IsChecked == true ? CommentaryPreset.Custom :
        CommentaryPreset.Balanced;

    private void SetCommentaryPreset(CommentaryPreset preset)
    {
        CommentaryOffRadioButton.IsChecked = preset == CommentaryPreset.Off;
        CommentaryQuietRadioButton.IsChecked = preset == CommentaryPreset.Quiet;
        CommentaryBalancedRadioButton.IsChecked = preset == CommentaryPreset.Balanced;
        CommentaryTalkativeRadioButton.IsChecked = preset == CommentaryPreset.Talkative;
        CommentaryCustomRadioButton.IsChecked = preset == CommentaryPreset.Custom;
    }

    private void OnVisionCheckBoxChecked(object sender, RoutedEventArgs e)
    {
        var openRouterSettings = _openRouterSettingsStore.Load();
        if (string.IsNullOrWhiteSpace(openRouterSettings.ApiKey)
            || string.IsNullOrWhiteSpace(openRouterSettings.VisionModelId))
        {
            if (sender is System.Windows.Controls.CheckBox checkBox)
            {
                checkBox.IsChecked = false;
            }

            System.Windows.MessageBox.Show(
                "Vision analysis requires an OpenRouter API key and vision model.\n\nConfigure these in the Cloud Providers tab first.",
                "Vision Not Configured",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        UpdatePerAppColumnEnabledState();
    }

    private void OnAudioCheckBoxToggled(object sender, RoutedEventArgs e)
    {
        UpdatePerAppColumnEnabledState();
    }

    private void OnMetadataCheckBoxToggled(object sender, RoutedEventArgs e)
    {
        UpdatePerAppColumnEnabledState();
    }

    private void OnVisionCheckBoxToggled(object sender, RoutedEventArgs e)
    {
        UpdatePerAppColumnEnabledState();
    }

    private void UpdatePerAppColumnEnabledState()
    {
        var systemRow = _observationRows.FirstOrDefault(r => r.ExecutablePath == SystemRowExecutablePath);
        if (systemRow is null) return;

        var metadataOverridden = systemRow.AllowMetadata;
        var visionOverridden = systemRow.AllowVisual;
        var audioOverridden = systemRow.AllowAudio;

        foreach (var row in _observationRows)
        {
            if (row.ExecutablePath == SystemRowExecutablePath) continue;
            row.IsMetadataEnabled = !metadataOverridden;
            row.IsVisionEnabled = !visionOverridden;
            row.IsAudioEnabled = !audioOverridden;
        }
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

    private void OnVisionDetailSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingVisionDetail || VisionDetailValueText is null) return;
        _syncingVisionDetail = true;
        var value = (int)Math.Round(e.NewValue);
        VisionDetailValueText.Text = value.ToString();
        UpdateVisionDetailLegend(value);
        _syncingVisionDetail = false;
    }

    private void OnVisionVerbositySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingVisionVerbosity || VisionVerbosityValueText is null) return;
        _syncingVisionVerbosity = true;
        var value = (int)Math.Round(e.NewValue);
        VisionVerbosityValueText.Text = value.ToString();
        UpdateVisionVerbosityLegend(value);
        _syncingVisionVerbosity = false;
    }

    private static void UpdateVisionDetailLegend(int value)
    {
    }

    private static void UpdateVisionVerbosityLegend(int value)
    {
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
}

public sealed class ApplicationRuleRow : INotifyPropertyChanged
{
    private bool _isDenied;
    private bool _allowMetadata;
    private bool _allowStructure;
    private bool _allowVisual;
    private bool _allowAudio;
    private bool _isMetadataEnabled = true;
    private bool _isVisionEnabled = true;
    private bool _isAudioEnabled = true;

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

    public bool AllowAudio
    {
        get => _allowAudio;
        set
        {
            if (SetField(ref _allowAudio, value) && value)
            {
                IsDenied = false;
            }
        }
    }

    public bool IsAudioEnabled
    {
        get => _isAudioEnabled;
        set => SetField(ref _isAudioEnabled, value);
    }

    public bool IsMetadataEnabled
    {
        get => _isMetadataEnabled;
        set => SetField(ref _isMetadataEnabled, value);
    }

    public bool IsVisionEnabled
    {
        get => _isVisionEnabled;
        set => SetField(ref _isVisionEnabled, value);
    }

    public bool HasDecision => AllowMetadata || AllowStructure || AllowVisual || AllowAudio;

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
            _allowVisual = rule.AllowVisual,
            _allowAudio = rule.AllowAudio
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
            AllowVisual,
            AllowAudio);
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
