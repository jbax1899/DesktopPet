using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DesktopPet.App.Audio;

internal abstract class WasapiCaptureSource : IAudioCaptureSource
{
    private IWaveIn? _capture;
    private MMDevice? _device;
    private readonly string? _deviceId;
    private bool _stopping;
    private bool _disposed;

    protected WasapiCaptureSource(AudioSourceKind kind, string? deviceId = null)
    {
        Kind = kind;
        _deviceId = deviceId;
    }

    public AudioSourceKind Kind { get; }

    public string? DeviceName { get; private set; }

    public string? FormatDescription { get; private set; }

    public event EventHandler<AudioSamplesAvailableEventArgs>? SamplesAvailable;

    public event EventHandler<Exception?>? CaptureFailed;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_capture is not null)
        {
            return;
        }

        try
        {
            _stopping = false;
            using var enumerator = new MMDeviceEnumerator();
            _device = string.IsNullOrEmpty(_deviceId)
                ? GetDefaultDevice(enumerator)
                : enumerator.GetDevice(_deviceId);
            DeviceName = _device.FriendlyName;
            _capture = CreateCapture(_device);
            FormatDescription = FormatWaveFormat(_capture.WaveFormat);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
        }
        catch
        {
            CleanupCapture();
            throw;
        }
    }

    public void Stop()
    {
        if (_capture is null)
        {
            return;
        }

        _stopping = true;
        try
        {
            _capture.StopRecording();
        }
        catch
        {
        }
        finally
        {
            CleanupCapture();
            _stopping = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    protected abstract MMDevice GetDefaultDevice(MMDeviceEnumerator enumerator);

    protected abstract IWaveIn CreateCapture(MMDevice device);

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var capture = _capture;
        if (capture is null || e.BytesRecorded <= 0)
        {
            return;
        }

        try
        {
            var samples = AudioSampleConverter.ToMono(e.Buffer.AsSpan(0, e.BytesRecorded), capture.WaveFormat);
            if (samples.Length > 0)
            {
                SamplesAvailable?.Invoke(
                    this,
                    new AudioSamplesAvailableEventArgs(samples, capture.WaveFormat.SampleRate, DateTimeOffset.UtcNow));
            }
        }
        catch (Exception ex)
        {
            CaptureFailed?.Invoke(this, ex);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        var expected = _stopping;
        CleanupCapture();
        if (!expected)
        {
            CaptureFailed?.Invoke(
                this,
                e.Exception ?? new InvalidOperationException("Audio capture stopped unexpectedly."));
        }
    }

    private void CleanupCapture()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _capture = null;
        }

        _device?.Dispose();
        _device = null;
    }

    private static string FormatWaveFormat(WaveFormat format)
    {
        return $"{format.SampleRate:N0} Hz, {format.BitsPerSample}-bit, {format.Channels} channel"
            + (format.Channels == 1 ? string.Empty : "s");
    }
}

internal sealed class MicrophoneCaptureSource : WasapiCaptureSource
{
    public MicrophoneCaptureSource(string? deviceId = null)
        : base(AudioSourceKind.Microphone, deviceId)
    {
    }

    protected override MMDevice GetDefaultDevice(MMDeviceEnumerator enumerator) =>
        enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);

    protected override IWaveIn CreateCapture(MMDevice device) => new WasapiCapture(device);
}

internal sealed class SystemLoopbackCaptureSource : WasapiCaptureSource
{
    public SystemLoopbackCaptureSource(string? deviceId = null)
        : base(AudioSourceKind.SystemAudio, deviceId)
    {
    }

    protected override MMDevice GetDefaultDevice(MMDeviceEnumerator enumerator) =>
        enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

    protected override IWaveIn CreateCapture(MMDevice device) => new WasapiLoopbackCapture(device);
}
