using System.Windows;
using DesktopPet.App.Audio;

namespace DesktopPet.App.Settings;

public partial class SettingsWindow
{
    private void OnAudioDiagnosticsTick(object? sender, EventArgs e)
    {
        RefreshAudioDiagnostics();
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

    private void RefreshAudioDiagnostics()
    {
        if (MicrophoneDiagnosticTextBlock is null || SystemAudioDiagnosticTextBlock is null)
        {
            return;
        }

        MicrophoneDiagnosticTextBlock.Text = FormatAudioDiagnostic(
            _audioCaptureCoordinator.GetDiagnostic(AudioSourceKind.Microphone));
        SystemAudioDiagnosticTextBlock.Text = FormatAudioDiagnostic(
            _audioCaptureCoordinator.GetDiagnostic(AudioSourceKind.SystemAudio));
        AnalysisDiagnosticTextBlock.Text = FormatAnalysisDiagnostic(
            _audioAnalysisCoordinator.Diagnostic);
    }

    private static string FormatAudioDiagnostic(AudioSourceDiagnostic diagnostic)
    {
        var details = new List<string>
        {
            diagnostic.State.ToString()
        };

        if (!string.IsNullOrWhiteSpace(diagnostic.DeviceName))
        {
            details.Add(diagnostic.DeviceName);
        }

        if (!string.IsNullOrWhiteSpace(diagnostic.Format))
        {
            details.Add(diagnostic.Format);
        }

        details.Add($"level {diagnostic.CurrentLevel:P0}");
        details.Add($"active {diagnostic.ActiveSegmentDuration.TotalSeconds:0.0}s");
        details.Add($"{diagnostic.CompletedCount} completed");
        details.Add($"{diagnostic.DiscardedCount} discarded");

        if (!string.IsNullOrWhiteSpace(diagnostic.LastError))
        {
            details.Add($"error: {diagnostic.LastError}");
        }

        return string.Join(" · ", details);
    }

    private static string FormatAnalysisDiagnostic(AudioAnalysisDiagnostic diagnostic)
    {
        var state = !diagnostic.Enabled
            ? "Disabled"
            : !diagnostic.AnalyzerAvailable
                ? "Unavailable"
                : diagnostic.RequestActive
                    ? "Analyzing"
                    : "Ready";
        var details = new List<string>
        {
            state,
            $"{diagnostic.QueueDepth} queued",
            $"{diagnostic.SuccessfulCount} successful",
            $"{diagnostic.FailureCount} failed",
            $"{diagnostic.DroppedCount} dropped"
        };

        if (diagnostic.LastSuccessAt.HasValue)
        {
            details.Add($"last success {diagnostic.LastSuccessAt.Value.LocalDateTime:g}");
        }

        if (!string.IsNullOrWhiteSpace(diagnostic.LastSafeFailure))
        {
            details.Add(diagnostic.LastSafeFailure);
        }

        return string.Join(" · ", details);
    }
}
