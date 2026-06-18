using System.IO;
using System.Text.Json;
using DesktopPet.App.Security;
using DesktopPet.App.Storage;

namespace DesktopPet.App.Cloud;

public sealed class OpenRouterSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly JsonFileStore<OpenRouterSettings> _settingsFile;
    private readonly CredentialStore _credentialStore;

    public OpenRouterSettingsStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopPet",
                "openrouter-settings.json"),
            new CredentialStore())
    {
    }

    internal OpenRouterSettingsStore(string settingsFilePath, CredentialStore credentialStore)
    {
        _settingsFile = new JsonFileStore<OpenRouterSettings>(
            settingsFilePath,
            json => JsonSerializer.Deserialize<OpenRouterSettings>(json, JsonOptions)
                ?? throw new JsonException("OpenRouter settings are empty."),
            settings => JsonSerializer.Serialize(settings, JsonOptions));
        _credentialStore = credentialStore;
    }

    public OpenRouterSettings Load()
    {
        var apiKey = _credentialStore.GetOpenRouterApiKey();
        return _settingsFile.Load(EmptySettings()) with { ApiKey = apiKey };
    }

    public void Save(OpenRouterSettings settings)
    {
        _credentialStore.SaveOpenRouterApiKey(settings.ApiKey);

        _settingsFile.Save(settings);
    }

    private static OpenRouterSettings EmptySettings()
    {
        return new OpenRouterSettings(null, null);
    }
}
