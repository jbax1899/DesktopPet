using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using DesktopPet.App.Memory;

namespace DesktopPet.App.Observation;

public partial class ObservationSettingsWindow : Window
{
    private readonly IObservationPermissionService _permissionService;
    private readonly IDesktopObservationCoordinator _observationCoordinator;
    private readonly AmbientDecisionStore _decisionStore;
    private readonly IMemoryStore _memoryStore;
    private readonly ObservableCollection<ApplicationRuleRow> _rows = [];

    public ObservationSettingsWindow(
        IObservationPermissionService permissionService,
        IDesktopObservationCoordinator observationCoordinator,
        AmbientDecisionStore decisionStore,
        IMemoryStore memoryStore)
    {
        _permissionService = permissionService;
        _observationCoordinator = observationCoordinator;
        _decisionStore = decisionStore;
        _memoryStore = memoryStore;
        InitializeComponent();
        CommentaryLevelComboBox.ItemsSource = Enum.GetValues<CommentaryLevel>();
        ApplicationsGrid.ItemsSource = _rows;
        LoadSettings();
    }

    private void OnRecentObservationsClicked(object sender, RoutedEventArgs e)
    {
        var window = new RecentObservationsWindow(_observationCoordinator, _memoryStore)
        {
            Owner = this
        };
        window.Show();
    }

    private void OnRecentDecisionsClicked(object sender, RoutedEventArgs e)
    {
        var window = new AmbientDecisionsWindow(_decisionStore, _memoryStore)
        {
            Owner = this
        };
        window.Show();
    }

    private void LoadSettings()
    {
        var settings = _permissionService.Current;
        ObservationEnabledCheckBox.IsChecked = settings.ObservationEnabled;
        AmbientCommentsEnabledCheckBox.IsChecked = settings.AmbientCommentsEnabled;
        DoNotDisturbCheckBox.IsChecked = settings.DoNotDisturb;
        CommentaryLevelComboBox.SelectedItem = settings.CommentaryLevel;

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
        StatusTextBlock.Text = "Running applications refreshed.";
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
            DoNotDisturb = DoNotDisturbCheckBox.IsChecked == true,
            CommentaryLevel = CommentaryLevelComboBox.SelectedItem is CommentaryLevel level
                ? level
                : CommentaryLevel.Balanced,
            ApplicationRules = rules
        });

        StatusTextBlock.Text = "Screen context permissions saved.";
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
                AllowMetadata = true;
            }
        }
    }

    public bool HasDecision => IsDenied || AllowMetadata || AllowStructure || AllowVisual;

    public event PropertyChangedEventHandler? PropertyChanged;

    public static ApplicationRuleRow FromRule(ApplicationObservationRule rule)
    {
        return new ApplicationRuleRow
        {
            ExecutablePath = rule.ExecutablePath,
            DisplayName = rule.DisplayName,
            _isDenied = rule.IsDenied,
            _allowMetadata = rule.AllowMetadata,
            _allowStructure = rule.AllowStructure,
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
