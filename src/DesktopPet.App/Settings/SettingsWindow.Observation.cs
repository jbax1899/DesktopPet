using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using DesktopPet.App.Observation;

namespace DesktopPet.App.Settings;

public partial class SettingsWindow
{
    private void LoadObservationSettings()
    {
        var settings = _permissionService.Current;
        _loadingObservationSettings = true;
        ObservationEnabledCheckBox.IsChecked = settings.ObservationEnabled;
        AmbientCommentsEnabledCheckBox.IsChecked = settings.AmbientCommentsEnabled;
        CaptureScreenshotOnChatSendCheckBox.IsChecked = settings.CaptureScreenshotOnChatSend;
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
            CaptureScreenshotOnChatSend = CaptureScreenshotOnChatSendCheckBox.IsChecked == true,
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
