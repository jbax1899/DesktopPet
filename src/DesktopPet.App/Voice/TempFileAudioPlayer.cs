using System.IO;
using System.Windows.Media;

namespace DesktopPet.App.Voice;

public sealed class TempFileAudioPlayer : IDisposable
{
    private readonly MediaPlayer _mediaPlayer = new();

    private string? _currentTempFilePath;

    public async Task PlayAsync(byte[] audioBytes, string audioFormat, CancellationToken cancellationToken)
    {
        if (!string.Equals(audioFormat, "mp3", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported audio format: {audioFormat}");
        }

        StopAndClearCurrentFile();

        // MediaPlayer wants a file or URI, not raw MP3 bytes.
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"DesktopPet-{Guid.NewGuid():N}.mp3");
        await File.WriteAllBytesAsync(tempFilePath, audioBytes, cancellationToken);
        _currentTempFilePath = tempFilePath;

        // Turn MediaPlayer's events into something this method can await.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler endedHandler = (_, _) => completion.TrySetResult();
        EventHandler<ExceptionEventArgs> failedHandler = (_, e) => completion.TrySetException(e.ErrorException);

        _mediaPlayer.MediaEnded += endedHandler;
        _mediaPlayer.MediaFailed += failedHandler;

        try
        {
            _mediaPlayer.Open(new Uri(tempFilePath));
            _mediaPlayer.Play();
            await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _mediaPlayer.MediaEnded -= endedHandler;
            _mediaPlayer.MediaFailed -= failedHandler;
            StopAndClearCurrentFile();
        }
    }

    public void Dispose()
    {
        StopAndClearCurrentFile();
        _mediaPlayer.Close();
    }

    private void StopAndClearCurrentFile()
    {
        _mediaPlayer.Stop();
        _mediaPlayer.Close();

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
}
