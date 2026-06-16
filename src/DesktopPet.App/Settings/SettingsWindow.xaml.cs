using DesktopPet.App.Cloud;
using System.Windows;

namespace DesktopPet.App.Settings;

public partial class SettingsWindow : Window
{
    private readonly CloudAiSettingsStore _settingsStore;

    public SettingsWindow(CloudAiSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;

        InitializeComponent();
        LoadSettings();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _settingsStore.Save(new CloudAiSettings(
                ToNullIfWhiteSpace(ElevenLabsApiKeyTextBox.Text),
                ToNullIfWhiteSpace(ElevenLabsAgentIdTextBox.Text),
                ToNullIfWhiteSpace(ElevenLabsVoiceIdTextBox.Text)));

            StatusTextBlock.Text = "Saved.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Save failed: {ex.Message}";
        }
    }

    private void LoadSettings()
    {
        var settings = _settingsStore.Load();
        ElevenLabsApiKeyTextBox.Text = settings.ElevenLabsApiKey ?? string.Empty;
        ElevenLabsAgentIdTextBox.Text = settings.ElevenLabsAgentId ?? string.Empty;
        ElevenLabsVoiceIdTextBox.Text = settings.ElevenLabsVoiceId ?? string.Empty;
    }

    private static string? ToNullIfWhiteSpace(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
