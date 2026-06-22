using System.IO;
using System.Net.Http;
using DesktopPet.App.Audio;
using DesktopPet.App.Cloud;
using DesktopPet.App.Settings;
using DesktopPet.App.Voice;

namespace DesktopPet.App.Shell;

public sealed class AudioServiceFactory
{
    public TranscriptWorkingBuffer TranscriptWorkingBuffer { get; }
    public AudioObservationStore AudioObservationStore { get; }
    public AudioObservationContextProvider AudioObservationContextProvider { get; }
    public IAudioSegmentAnalyzer AudioSegmentAnalyzer { get; }
    public AudioAnalysisCoordinator AudioAnalysisCoordinator { get; }
    public AudioCaptureCoordinator AudioCaptureCoordinator { get; }
    public StreamingMp3AudioPlayer AudioPlayer { get; }

    public AudioServiceFactory(
        HttpClient httpClient,
        SettingsHub settings)
    {
        TranscriptWorkingBuffer = new TranscriptWorkingBuffer(
            () => TimeSpan.FromSeconds(
                settings.AudioContext.Load().Normalize().TranscriptRetentionSeconds));
        AudioObservationStore = new AudioObservationStore(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopPet",
                "audio-observations.json"),
            () => settings.AudioContext.Load().Normalize().StoredObservationCount);
        AudioObservationContextProvider = new AudioObservationContextProvider(
            AudioObservationStore,
            TranscriptWorkingBuffer,
            settings.AudioContext.Load);
        AudioSegmentAnalyzer = new OpenRouterSttAnalyzer(
            httpClient,
            settings.OpenRouter.Load,
            settings.AudioContext.Load);
        AudioAnalysisCoordinator = new AudioAnalysisCoordinator(
            AudioSegmentAnalyzer,
            TranscriptWorkingBuffer,
            AudioObservationStore);
        AudioCaptureCoordinator = new AudioCaptureCoordinator(
            (kind, deviceId) => kind switch
            {
                AudioSourceKind.Microphone => new MicrophoneCaptureSource(deviceId),
                AudioSourceKind.SystemAudio => new SystemLoopbackCaptureSource(deviceId),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            },
            AudioAnalysisCoordinator,
            processLoopbackFactory: processId => new ProcessLoopbackCaptureSource(processId));
        AudioPlayer = new StreamingMp3AudioPlayer();
    }

    public void Dispose()
    {
        AudioCaptureCoordinator.Dispose();
        AudioAnalysisCoordinator.Dispose();
        AudioPlayer.Dispose();
    }
}
