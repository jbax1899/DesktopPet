using NAudio.Wave;
using System.IO;

namespace DesktopPet.App.Voice;

public sealed class TempFileAudioPlayer : IDisposable
{
    private static readonly TimeSpan AmplitudeFrameDuration = TimeSpan.FromMilliseconds(33);
    private const double NoiseGate = 0.08;
    private const double Attack = 0.75;
    private const double Release = 0.35;

    private WaveOutEvent? _waveOut;
    private string? _currentTempFilePath;

    public async Task PlayAsync(
        byte[] audioBytes,
        string audioFormat,
        CancellationToken cancellationToken,
        Action<double>? mouthOpenChanged = null)
    {
        if (!string.Equals(audioFormat, "mp3", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported audio format: {audioFormat}");
        }

        StopAndClearCurrentFile();

        // NAudio's MP3 reader is file-backed for this simple prototype path.
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"DesktopPet-{Guid.NewGuid():N}.mp3");
        await File.WriteAllBytesAsync(tempFilePath, audioBytes, cancellationToken);
        _currentTempFilePath = tempFilePath;

        var amplitudeTimeline = mouthOpenChanged is null
            ? []
            : BuildAmplitudeTimeline(tempFilePath);

        using var reader = new AudioFileReader(tempFilePath);
        using var waveOut = new WaveOutEvent();
        _waveOut = waveOut;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        waveOut.PlaybackStopped += OnPlaybackStopped;
        waveOut.Init(reader);

        try
        {
            waveOut.Play();
            await ReportAmplitudeUntilCompleteAsync(amplitudeTimeline, mouthOpenChanged, completion.Task, cancellationToken);
        }
        finally
        {
            waveOut.PlaybackStopped -= OnPlaybackStopped;
            mouthOpenChanged?.Invoke(0);
            StopAndClearCurrentFile();
        }

        return;

        void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception is null)
            {
                completion.TrySetResult();
            }
            else
            {
                completion.TrySetException(e.Exception);
            }
        }
    }

    public void Dispose()
    {
        StopAndClearCurrentFile();
    }

    private void StopAndClearCurrentFile()
    {
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;

        if (_currentTempFilePath is null)
        {
            return;
        }

        try
        {
            File.Delete(_currentTempFilePath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        _currentTempFilePath = null;
    }

    private static async Task ReportAmplitudeUntilCompleteAsync(
        IReadOnlyList<double> amplitudeTimeline,
        Action<double>? mouthOpenChanged,
        Task playbackTask,
        CancellationToken cancellationToken)
    {
        if (mouthOpenChanged is null)
        {
            await playbackTask.WaitAsync(cancellationToken);
            return;
        }

        var frameDelay = AmplitudeFrameDuration;
        for (var frameIndex = 0; !playbackTask.IsCompleted; frameIndex++)
        {
            var openness = frameIndex < amplitudeTimeline.Count ? amplitudeTimeline[frameIndex] : 0;
            mouthOpenChanged(openness);

            var delayTask = Task.Delay(frameDelay, cancellationToken);
            var completedTask = await Task.WhenAny(playbackTask, delayTask);
            if (completedTask == playbackTask)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        await playbackTask.WaitAsync(cancellationToken);
    }

    private static double[] BuildAmplitudeTimeline(string audioFilePath)
    {
        using var reader = new AudioFileReader(audioFilePath);

        var samplesPerFrame = Math.Max(1, (int)Math.Round(reader.WaveFormat.SampleRate * AmplitudeFrameDuration.TotalSeconds));
        var frameBuffer = new float[samplesPerFrame * reader.WaveFormat.Channels];
        var rmsFrames = new List<double>();

        while (true)
        {
            var samplesRead = reader.Read(frameBuffer, 0, frameBuffer.Length);
            if (samplesRead == 0)
            {
                break;
            }

            double sumSquares = 0;
            for (var i = 0; i < samplesRead; i++)
            {
                sumSquares += frameBuffer[i] * frameBuffer[i];
            }

            rmsFrames.Add(Math.Sqrt(sumSquares / samplesRead));
        }

        if (rmsFrames.Count == 0)
        {
            return [];
        }

        var normalizationLevel = GetPercentile(rmsFrames, 0.9);
        if (normalizationLevel <= 0)
        {
            return new double[rmsFrames.Count];
        }

        var timeline = new double[rmsFrames.Count];
        double smoothed = 0;

        for (var i = 0; i < rmsFrames.Count; i++)
        {
            var normalized = Math.Clamp(rmsFrames[i] / normalizationLevel, 0, 1);
            var gated = normalized < NoiseGate ? 0 : (normalized - NoiseGate) / (1 - NoiseGate);
            var smoothing = gated > smoothed ? Attack : Release;
            smoothed += (gated - smoothed) * smoothing;
            timeline[i] = smoothed;
        }

        return timeline;
    }

    private static double GetPercentile(IReadOnlyCollection<double> values, double percentile)
    {
        var sortedValues = values.Order().ToArray();
        var index = (int)Math.Round((sortedValues.Length - 1) * percentile);
        return sortedValues[Math.Clamp(index, 0, sortedValues.Length - 1)];
    }
}
