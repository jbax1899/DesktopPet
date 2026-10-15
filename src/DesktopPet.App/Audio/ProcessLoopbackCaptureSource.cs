using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DesktopPet.App.Audio;

internal sealed class ProcessLoopbackCaptureSource : IAudioCaptureSource
{
    private static readonly WaveFormat CaptureWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

    private readonly object _sync = new();
    private readonly int _processId;
    private readonly bool _includeProcessTree;
    private CancellationTokenSource? _cts;
    private Task? _startTask;
    private WasapiRecorder? _recorder;
    private bool _stopping;
    private bool _disposed;

    public ProcessLoopbackCaptureSource(int processId, bool includeProcessTree = true)
    {
        _processId = processId;
        _includeProcessTree = includeProcessTree;
        Kind = AudioSourceKind.ProcessAudio;
    }

    public AudioSourceKind Kind { get; }

    public string? DeviceName => $"Process {_processId}";

    public string? FormatDescription => "44,100 Hz, 32-bit float, 2 channels";

    public event EventHandler<AudioSamplesAvailableEventArgs>? SamplesAvailable;

    public event EventHandler<Exception?>? CaptureFailed;

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopping || _recorder is not null || _startTask is { IsCompleted: false })
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _startTask = StartAsync(_cts.Token);
        }
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        WasapiRecorder? recorder = null;
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                throw new PlatformNotSupportedException(
                    "Process loopback capture requires Windows 10 build 19041 or later.");
            }

            var activeRecorder = await new WasapiRecorderBuilder()
                .WithProcessLoopback(
                    (uint)_processId,
                    _includeProcessTree
                        ? ProcessLoopbackMode.IncludeTargetProcessTree
                        : ProcessLoopbackMode.ExcludeTargetProcessTree)
                .WithFormat(CaptureWaveFormat)
                .BuildAsync()
                .ConfigureAwait(false);
            recorder = activeRecorder;

            lock (_sync)
            {
                if (_disposed || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                activeRecorder.DataAvailable += (buffer, _, _, _) =>
                    OnPcmFrame(buffer, activeRecorder.WaveFormat);
                activeRecorder.RecordingStopped += OnRecordingStopped;
                _recorder = activeRecorder;
                try
                {
                    activeRecorder.StartRecording();
                    recorder = null;
                }
                catch
                {
                    _recorder = null;
                    activeRecorder.RecordingStopped -= OnRecordingStopped;
                    throw;
                }
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            CaptureFailed?.Invoke(this, ex);
        }
        finally
        {
            if (recorder is not null)
            {
                try
                {
                    await recorder.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_sync)
        {
            if (_disposed || _stopping || _cts?.IsCancellationRequested == true)
            {
                return;
            }
        }

        CaptureFailed?.Invoke(
            this,
            e.Exception ?? new InvalidOperationException("Process audio capture stopped unexpectedly."));
    }

    private void OnPcmFrame(ReadOnlySpan<byte> buffer, WaveFormat waveFormat)
    {
        lock (_sync)
        {
            if (_disposed || _stopping || _cts?.IsCancellationRequested == true)
            {
                return;
            }
        }

        try
        {
            var samples = AudioSampleConverter.ToMono(buffer, waveFormat);
            if (samples.Length > 0)
            {
                SamplesAvailable?.Invoke(
                    this,
                    new AudioSamplesAvailableEventArgs(samples, waveFormat.SampleRate, DateTimeOffset.UtcNow));
            }
        }
        catch (Exception ex)
        {
            CaptureFailed?.Invoke(this, ex);
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        WasapiRecorder? recorder;
        lock (_sync)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
            cts = _cts;
            _cts = null;
            cts?.Cancel();
            recorder = _recorder;
            _recorder = null;
        }

        try
        {
            if (recorder is null)
            {
                return;
            }

            recorder.RecordingStopped -= OnRecordingStopped;
            try
            {
                recorder.StopRecording();
            }
            catch
            {
            }

            try
            {
                recorder.Dispose();
            }
            catch
            {
            }
        }
        finally
        {
            cts?.Dispose();
            lock (_sync)
            {
                _stopping = false;
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
    }
}
