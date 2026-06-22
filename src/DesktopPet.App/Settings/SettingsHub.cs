namespace DesktopPet.App.Settings;

public sealed class SettingsHub
{
    public ProfileSettingsStore Profile { get; }
    public UiSettingsStore Ui { get; }
    public Audio.AudioContextSettingsStore AudioContext { get; }
    public Cloud.ElevenLabsSettingsStore ElevenLabs { get; }
    public Cloud.OpenRouterSettingsStore OpenRouter { get; }
    public Observation.ObservationSettingsStore Observation { get; }

    public event Action<string>? Saved;

    public SettingsHub()
    {
        Profile = new ProfileSettingsStore();
        Ui = new UiSettingsStore();
        AudioContext = new Audio.AudioContextSettingsStore();
        ElevenLabs = new Cloud.ElevenLabsSettingsStore();
        OpenRouter = new Cloud.OpenRouterSettingsStore();
        Observation = new Observation.ObservationSettingsStore();
    }

    internal SettingsHub(
        ProfileSettingsStore profile,
        UiSettingsStore ui,
        Audio.AudioContextSettingsStore audioContext,
        Cloud.ElevenLabsSettingsStore elevenLabs,
        Cloud.OpenRouterSettingsStore openRouter,
        Observation.ObservationSettingsStore observation)
    {
        Profile = profile;
        Ui = ui;
        AudioContext = audioContext;
        ElevenLabs = elevenLabs;
        OpenRouter = openRouter;
        Observation = observation;
    }

    public void SaveProfile(Settings.ProfileSettings settings)
    {
        Profile.Save(settings);
        Saved?.Invoke(nameof(Profile));
    }

    public void SaveUi(Settings.UiSettings settings)
    {
        Ui.Save(settings);
        Saved?.Invoke(nameof(Ui));
    }

    public void SaveAudioContext(Audio.AudioContextSettings settings)
    {
        AudioContext.Save(settings);
        Saved?.Invoke(nameof(AudioContext));
    }

    public void SaveElevenLabs(Cloud.ElevenLabsSettings settings)
    {
        ElevenLabs.Save(settings);
        Saved?.Invoke(nameof(ElevenLabs));
    }

    public void SaveOpenRouter(Cloud.OpenRouterSettings settings)
    {
        OpenRouter.Save(settings);
        Saved?.Invoke(nameof(OpenRouter));
    }

    public void SaveObservation(Observation.ObservationSettings settings)
    {
        Observation.Save(settings);
        Saved?.Invoke(nameof(Observation));
    }
}
