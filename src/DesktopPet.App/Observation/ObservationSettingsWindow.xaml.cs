using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

namespace DesktopPet.App.Observation;

public partial class ObservationSettingsWindow : Window
{
    private readonly IObservationPermissionService _permissionService;
    private readonly ObservableCollection<ApplicationRuleRow> _rows = [];

    public ObservationSettingsWindow(IObservationPermissionService permissionService)
    {
        _permissionService = permissionService;
        InitializeComponent();
        ApplicationsGrid.ItemsSource = _rows;
        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _permissionService.Current;
        ObservationEnabledCheckBox.IsChecked = settings.ObservationEnabled;
        AmbientCommentsEnabledCheckBox.IsChecked = settings.AmbientCommentsEnabled;

        switch (settings.CooldownMinutes)
        {
            case <= 3:
                CommentaryTalkativeRadioButton.IsChecked = true;
                break;
            case >= 8:
                CommentaryQuietRadioButton.IsChecked = true;
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

        var rows = settings.ApplicationRules
            .Select(ApplicationRuleRow.FromRule)
            .ToDictionary(row => row.ExecutablePath, StringComparer.OrdinalIgnoreCase);

        foreach (var application in ListRunningApplications())
        {
            rows.TryAdd(application.ExecutablePath, application);
        }

        _rows.Clear();
        foreach (var row in rows.Values.OrderBy(row => row.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            _rows.Add(row);
        }
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        LoadSettings();
        StatusTextBlock.Text = string.Empty;
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        var current = _permissionService.Current;
        var rules = _rows
            .Where(row => row.HasDecision)
            .Select(row => row.ToRule())
            .ToArray();

        _permissionService.Save(current with
        {
            ObservationEnabled = ObservationEnabledCheckBox.IsChecked == true,
            AmbientCommentsEnabled = AmbientCommentsEnabledCheckBox.IsChecked == true,
            CooldownMinutes = CommentaryTalkativeRadioButton.IsChecked == true
                ? 2
                : CommentaryQuietRadioButton.IsChecked == true
                    ? 10
                    : 5,
            DuplicateWindowMinutes = CommentaryTalkativeRadioButton.IsChecked == true
                ? 3
                : CommentaryQuietRadioButton.IsChecked == true
                    ? 20
                    : 15,
            CheckInMinutes = CommentaryTalkativeRadioButton.IsChecked == true
                ? 3
                : CommentaryQuietRadioButton.IsChecked == true
                    ? 10
                    : 5,
            VisionSensitivity = VisionHighRadioButton.IsChecked == true
                ? VisionSensitivity.High
                : VisionLowRadioButton.IsChecked == true
                    ? VisionSensitivity.Low
                    : VisionSensitivity.Medium,
            ApplicationRules = rules
        });

        StatusTextBlock.Text = "Screen context permissions saved.";
    }

    private void OnCommentaryLevelChanged(object sender, RoutedEventArgs e)
    {
        if (CommentaryLegendTextBlock is null) return;
        CommentaryLegendTextBlock.Text = CommentaryQuietRadioButton.IsChecked == true
            ? "Comments every ~10 min. Check-in every 10 min. Duplicate topics suppressed for 20 min."
            : CommentaryTalkativeRadioButton.IsChecked == true
                ? "Comments every ~2 min. Check-in every 3 min. Duplicate topics suppressed for 10 min."
                : "Comments every ~5 min. Check-in every 5 min. Duplicate topics suppressed for 15 min.";
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
