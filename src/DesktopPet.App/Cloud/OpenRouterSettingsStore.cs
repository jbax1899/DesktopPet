using System.IO;
using System.Text.Json;

namespace DesktopPet.App.Cloud;

public sealed class OpenRouterSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    public OpenRouterSettingsStore()
    {
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet");

        _settingsFilePath = Path.Combine(settingsDirectory, "openrouter-settings.json");
    }

    public OpenRouterSettings Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return EmptySettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<OpenRouterSettings>(json, JsonOptions) ?? EmptySettings();
        }
        catch (JsonException)
        {
            return EmptySettings();
        }
        catch (IOException)
        {
            return EmptySettings();
        }
    }

    public void Save(OpenRouterSettings settings)
    {
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
