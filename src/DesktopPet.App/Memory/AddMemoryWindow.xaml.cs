using System.Windows;

namespace DesktopPet.App.Memory;

public partial class AddMemoryWindow : Window
{
    public AddMemoryWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => MemoryTextBox.Focus();
    }

    public string MemoryText => MemoryTextBox.Text.Trim();

    private void OnAddClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(MemoryText))
        {
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
