using System.IO;
using System.Text.Json;
using DesktopPet.App.Security;

namespace DesktopPet.App.Cloud;

public sealed class ElevenLabsSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;
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
        _settingsFilePath = settingsFilePath;
        _credentialStore = credentialStore;
    }

    public ElevenLabsSettings Load()
    {
        var apiKey = _credentialStore.GetElevenLabsApiKey();
        if (!File.Exists(_settingsFilePath))
        {
            return EmptySettings() with { ElevenLabsApiKey = apiKey };
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<ElevenLabsSettings>(json, JsonOptions) ?? EmptySettings();
            return settings with { ElevenLabsApiKey = apiKey };
        }
        catch (JsonException)
        {
            return EmptySettings() with { ElevenLabsApiKey = apiKey };
        }
        catch (IOException)
        {
            return EmptySettings() with { ElevenLabsApiKey = apiKey };
        }
    }

    public void Save(ElevenLabsSettings settings)
    {
        _credentialStore.SaveElevenLabsApiKey(settings.ElevenLabsApiKey);

        var directory = Path.GetDirectoryName(_settingsFilePath)
            ?? throw new InvalidOperationException("Settings file path does not have a directory.");

        Directory.CreateDirectory(directory);
        File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static ElevenLabsSettings EmptySettings()
    {
        return new ElevenLabsSettings(null, null, null, []);
    }
}
