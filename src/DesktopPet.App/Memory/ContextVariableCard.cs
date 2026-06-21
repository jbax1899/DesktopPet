using System.Windows;
using System.Windows.Controls;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace DesktopPet.App.Memory;

public sealed record ContextVariableView(string Name, string Value);

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
        panel.Children.Add(name);
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
