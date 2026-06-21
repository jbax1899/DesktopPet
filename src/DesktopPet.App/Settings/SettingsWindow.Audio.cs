using System.Windows;

namespace DesktopPet.App.Settings;

public partial class SettingsWindow
{
    private void OnAudioDiagnosticsTick(object? sender, EventArgs e)
    {
    }

    private void OnTranscriptVerbositySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingTranscriptVerbosity || TranscriptVerbosityValueText is null) return;
        _syncingTranscriptVerbosity = true;
        var value = (int)Math.Round(e.NewValue);
        TranscriptVerbosityValueText.Text = value.ToString();
        UpdateTranscriptVerbosityLegend(value);
        _syncingTranscriptVerbosity = false;
    }

    private static void UpdateTranscriptVerbosityLegend(int value)
    {
    }
}
