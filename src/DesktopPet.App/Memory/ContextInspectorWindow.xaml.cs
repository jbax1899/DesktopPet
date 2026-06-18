using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace DesktopPet.App.Memory;

public partial class ContextInspectorWindow : Window, INotifyPropertyChanged
{
    private readonly Func<AgentContextSnapshot> _liveSnapshotProvider;
    private string _historicalStatus = "Select a bot reply in Chat History to inspect its recorded context.";

    public ContextInspectorWindow(
        Func<AgentContextSnapshot> liveSnapshotProvider,
        ChatHistoryMessage? selectedMessage)
    {
        _liveSnapshotProvider = liveSnapshotProvider;
        InitializeComponent();
        DataContext = this;
        RefreshLivePreview();
        SetSelectedMessage(selectedMessage);
    }

    public ObservableCollection<ContextVariableView> LiveVariables { get; } = [];

    public ObservableCollection<ContextVariableView> HistoricalVariables { get; } = [];

    public string HistoricalStatus
    {
        get => _historicalStatus;
        private set
        {
            if (_historicalStatus == value)
            {
                return;
            }

            _historicalStatus = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetSelectedMessage(ChatHistoryMessage? message)
    {
        HistoricalVariables.Clear();

        if (message?.Role != ChatHistoryRole.Bot)
        {
            HistoricalStatus = "Select a bot reply in Chat History to inspect its recorded context.";
            return;
        }

        if (message.ContextSnapshot is null)
        {
            HistoricalStatus = "Context snapshot unavailable for this reply.";
            return;
        }

        HistoricalStatus = $"Context sent on {message.ContextSnapshot.CreatedAt.LocalDateTime:g}.";
        Populate(HistoricalVariables, message.ContextSnapshot);
    }

    private void OnRefreshLivePreviewClicked(object sender, RoutedEventArgs e)
    {
        RefreshLivePreview();
    }

    private void RefreshLivePreview()
    {
        LiveVariables.Clear();
        Populate(LiveVariables, _liveSnapshotProvider());
    }

    private static void Populate(
        ObservableCollection<ContextVariableView> target,
        AgentContextSnapshot snapshot)
    {
        foreach (var variable in snapshot.Values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            target.Add(new ContextVariableView(variable.Key, variable.Value));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record ContextVariableView(string Name, string Value)
{
    public string LengthLabel => $"{Value.Length:N0} characters";
}

public sealed class ContextVariableCard : ContentControl
{
    public ContextVariableCard()
    {
        var name = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Foreground = System.Windows.Media.Brushes.DarkSlateGray
        };
        name.SetBinding(TextBlock.TextProperty, nameof(ContextVariableView.Name));

        var length = new TextBlock
        {
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 11,
            Foreground = System.Windows.Media.Brushes.SlateGray
        };
        length.SetBinding(TextBlock.TextProperty, nameof(ContextVariableView.LengthLabel));

        var copyButton = new WpfButton
        {
            Content = "Copy",
            Padding = new Thickness(8, 2, 8, 2),
            MinWidth = 52
        };
        copyButton.Click += (_, _) =>
        {
            if (DataContext is ContextVariableView variable)
            {
                try
                {
                    System.Windows.Clipboard.SetText(variable.Value);
                }
                catch
                {
                }
            }
        };

        var header = new DockPanel();
        DockPanel.SetDock(copyButton, Dock.Right);
        header.Children.Add(copyButton);

        var labelPanel = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        labelPanel.Children.Add(name);
        labelPanel.Children.Add(length);
        header.Children.Add(labelPanel);

        var value = new WpfTextBox
        {
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(8, 6, 8, 6),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        value.SetBinding(WpfTextBox.TextProperty, nameof(ContextVariableView.Value));

        var panel = new StackPanel();
        panel.Children.Add(header);
        panel.Children.Add(value);

        Content = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(10),
            BorderBrush = System.Windows.Media.Brushes.LightSlateGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = panel
        };
    }
}
