using DesktopPet.App.Audio;
using DesktopPet.App.Cloud;
using DesktopPet.App.Observation;
using DesktopPet.App.Settings;

namespace DesktopPet.App.Tests;

[TestClass]
public sealed class SettingsHubTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SettingsHubTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Load_returns_defaults_for_each_store()
    {
        var hub = CreateHub();

        var profile = hub.Profile.Load();
        var ui = hub.Ui.Load();
        var audio = hub.AudioContext.Load();
        var elevenLabs = hub.ElevenLabs.Load();
        var openRouter = hub.OpenRouter.Load();
        var observation = hub.Observation.Load();

        Assert.AreEqual(ProfileSettings.Default, profile);
        Assert.AreEqual(UiSettings.Default.ChatShortcut, ui.ChatShortcut);
        Assert.AreEqual(AudioContextSettings.Default.Enabled, audio.Enabled);
        Assert.AreEqual(true, openRouter.RequireZeroRetention);
        Assert.AreEqual(ObservationSettings.Default.ObservationEnabled, observation.ObservationEnabled);
    }

    [TestMethod]
    public void Save_and_load_roundtrip_for_profile()
    {
        var hub = CreateHub();
        var settings = new ProfileSettings("Alice", "Buddy");

        hub.SaveProfile(settings);
        var loaded = hub.Profile.Load();

        Assert.AreEqual("Alice", loaded.UserName);
        Assert.AreEqual("Buddy", loaded.Nickname);
    }

    [TestMethod]
    public void Save_and_load_roundtrip_for_ui()
    {
        var hub = CreateHub();
        var settings = UiSettings.Default with
        {
            OverlayPosition = new OverlayPosition(100, 200)
        };

        hub.SaveUi(settings);
        var loaded = hub.Ui.Load();

        Assert.AreEqual(100, loaded.OverlayPosition?.Left);
        Assert.AreEqual(200, loaded.OverlayPosition?.Top);
    }

    [TestMethod]
    public void Save_and_load_roundtrip_for_audio_context()
    {
        var hub = CreateHub();
        var settings = AudioContextSettings.Default with
        {
            MicrophoneEnabled = true,
            ContextDepth = 10
        };

        hub.SaveAudioContext(settings);
        var loaded = hub.AudioContext.Load();

        Assert.IsTrue(loaded.MicrophoneEnabled);
        Assert.AreEqual(10, loaded.ContextDepth);
    }

    [TestMethod]
    public void Saved_event_fires_with_store_name()
    {
        var hub = CreateHub();
        var firedNames = new List<string>();
        hub.Saved += name => firedNames.Add(name);

        hub.SaveProfile(ProfileSettings.Default);
        hub.SaveUi(UiSettings.Default);
        hub.SaveAudioContext(AudioContextSettings.Default);

        CollectionAssert.AreEqual(new[] { "Profile", "Ui", "AudioContext" }, firedNames);
    }

    [TestMethod]
    public void Saved_event_fires_for_all_stores()
    {
        var hub = CreateHub();
        var firedNames = new List<string>();
        hub.Saved += name => firedNames.Add(name);

        hub.SaveElevenLabs(new ElevenLabsSettings(null, null, null, []));
        hub.SaveOpenRouter(new OpenRouterSettings(null, null, null));
        hub.SaveObservation(ObservationSettings.Default);

        CollectionAssert.AreEqual(new[] { "ElevenLabs", "OpenRouter", "Observation" }, firedNames);
    }

    [TestMethod]
    public void Func_delegates_return_fresh_data()
    {
        var hub = CreateHub();
        Func<ProfileSettings> loadProfile = hub.Profile.Load;

        var before = loadProfile();
        hub.SaveProfile(new ProfileSettings("Updated", null));
        var after = loadProfile();

        Assert.IsNull(before.UserName);
        Assert.AreEqual("Updated", after.UserName);
    }

    private SettingsHub CreateHub()
    {
        return new SettingsHub(
            new ProfileSettingsStore(Path.Combine(_tempDir, "profile.json")),
            new UiSettingsStore(Path.Combine(_tempDir, "ui.json")),
            new AudioContextSettingsStore(Path.Combine(_tempDir, "audio.json")),
            new ElevenLabsSettingsStore(Path.Combine(_tempDir, "elevenlabs.json"), new DesktopPet.App.Security.CredentialStore()),
            new OpenRouterSettingsStore(Path.Combine(_tempDir, "openrouter.json"), new DesktopPet.App.Security.CredentialStore()),
            new ObservationSettingsStore(Path.Combine(_tempDir, "observation.json")));
    }
}
