using System.IO;
using System.Text.Json;
using DesktopPet.App.Security;
using DesktopPet.App.Storage;

namespace DesktopPet.App.Cloud;

public sealed class ElevenLabsSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly JsonFileStore<ElevenLabsSettings> _settingsFile;
    private readonly CredentialStore _credentialStore;

    public ElevenLabsSettingsStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopPet",
                "cloud-ai-settings.json"),
            new CredentialStore())
    {
    }

    internal ElevenLabsSettingsStore(string settingsFilePath, CredentialStore credentialStore)
    {
        _settingsFile = new JsonFileStore<ElevenLabsSettings>(
            settingsFilePath,
            json => JsonSerializer.Deserialize<ElevenLabsSettings>(json, JsonOptions)
                ?? throw new JsonException("ElevenLabs settings are empty."),
            settings => JsonSerializer.Serialize(settings, JsonOptions));
        _credentialStore = credentialStore;
    }

    public ElevenLabsSettings Load()
    {
        var apiKey = _credentialStore.GetElevenLabsApiKey();
        return _settingsFile.Load(EmptySettings()) with { ElevenLabsApiKey = apiKey };
    }

    public void Save(ElevenLabsSettings settings)
    {
        _credentialStore.SaveElevenLabsApiKey(settings.ElevenLabsApiKey);

        _settingsFile.Save(settings);
    }

    private static ElevenLabsSettings EmptySettings()
    {
        return new ElevenLabsSettings(null, null, null, []);
    }
}
