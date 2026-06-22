using DesktopPet.Audio.ProcessLoopback.Native;

namespace DesktopPet.App.Audio;

// IAudioCaptureSource implementation that captures audio from a specific process
// using the Windows process loopback API (ActivateAudioInterfaceAsync with
// AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK).
//
// TODO: Replace with NAudio's built-in process loopback once NAudio 3.x ships
// support (PR #1225 / WasapiCapture.CreateForProcessCaptureAsync).
internal sealed class ProcessLoopbackCaptureSource : IAudioCaptureSource
{
    private static readonly NAudio.Wave.WaveFormat WaveFormat = new(44100, 16, 2);

    private readonly int _processId;
    private readonly bool _includeProcessTree;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public ProcessLoopbackCaptureSource(int processId, bool includeProcessTree = true)
    {
        _processId = processId;
        _includeProcessTree = includeProcessTree;
        Kind = AudioSourceKind.ProcessAudio;
    }

    public AudioSourceKind Kind { get; }

    public string? DeviceName => $"Process {_processId}";

    public string? FormatDescription => "44,100 Hz, 16-bit, 2 channels";

    public event EventHandler<AudioSamplesAvailableEventArgs>? SamplesAvailable;

    public event EventHandler<Exception?>? CaptureFailed;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            _cts = new CancellationTokenSource();
            await ProcessLoopbackCapture.StartAsync(
                _processId,
                _includeProcessTree,
                OnPcmFrame,
                _cts.Token);
        }
        catch (Exception ex)
        {
            CaptureFailed?.Invoke(this, ex);
        }
    }

    private void OnPcmFrame(byte[] buffer, int offset, int count)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var samples = AudioSampleConverter.ToMono(
                buffer.AsSpan(offset, count),
                WaveFormat);

            if (samples.Length > 0)
            {
                SamplesAvailable?.Invoke(
                    this,
                    new AudioSamplesAvailableEventArgs(samples, 44100, DateTimeOffset.UtcNow));
            }
        }
        catch (Exception ex)
        {
            CaptureFailed?.Invoke(this, ex);
        }
    }

    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        _cts?.Cancel();
        ProcessLoopbackCapture.Stop();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _cts?.Dispose();
        _cts = null;
        _disposed = true;
    }
}
