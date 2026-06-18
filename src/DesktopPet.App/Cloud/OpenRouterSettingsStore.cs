using System.IO;
using System.Text.Json;
using DesktopPet.App.Security;

namespace DesktopPet.App.Cloud;

public sealed class OpenRouterSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;
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
        _settingsFilePath = settingsFilePath;
        _credentialStore = credentialStore;
    }

    public OpenRouterSettings Load()
    {
        var apiKey = _credentialStore.GetOpenRouterApiKey();
        if (!File.Exists(_settingsFilePath))
        {
            return EmptySettings() with { ApiKey = apiKey };
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<OpenRouterSettings>(json, JsonOptions) ?? EmptySettings();
            return settings with { ApiKey = apiKey };
        }
        catch (JsonException)
        {
            return EmptySettings() with { ApiKey = apiKey };
        }
        catch (IOException)
        {
            return EmptySettings() with { ApiKey = apiKey };
        }
    }

    public void Save(OpenRouterSettings settings)
    {
        _credentialStore.SaveOpenRouterApiKey(settings.ApiKey);

        var directory = Path.GetDirectoryName(_settingsFilePath)
            ?? throw new InvalidOperationException("Settings file path does not have a directory.");

        Directory.CreateDirectory(directory);
        File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static OpenRouterSettings EmptySettings()
    {
        return new OpenRouterSettings(null, null);
    }
}
