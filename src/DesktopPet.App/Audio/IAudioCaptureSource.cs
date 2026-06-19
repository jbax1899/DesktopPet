namespace DesktopPet.App.Audio;

internal interface IAudioCaptureSource : IDisposable
{
    AudioSourceKind Kind { get; }

    string? DeviceName { get; }

    string? FormatDescription { get; }

    event EventHandler<AudioSamplesAvailableEventArgs>? SamplesAvailable;

    event EventHandler<Exception?>? CaptureFailed;

    void Start();

    void Stop();
}
