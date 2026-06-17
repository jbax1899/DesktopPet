using System.IO;
using System.Text.Json;

namespace DesktopPet.App.Cloud;

public sealed class ElevenLabsSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    public ElevenLabsSettingsStore()
    {
        // Plain JSON is temporary, so keep the storage detail boxed in here.
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet");

        _settingsFilePath = Path.Combine(settingsDirectory, "cloud-ai-settings.json");
    }

    public ElevenLabsSettings Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return EmptySettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<ElevenLabsSettings>(json, JsonOptions) ?? EmptySettings();
        }
        catch (JsonException)
        {
            // Start with empty settings if the file gets hand-edited badly.
            return EmptySettings();
        }
        catch (IOException)
        {
            return EmptySettings();
        }
    }

    public void Save(ElevenLabsSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsFilePath)
            ?? throw new InvalidOperationException("Settings file path does not have a directory.");

        Directory.CreateDirectory(directory);
        File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static ElevenLabsSettings EmptySettings()
    {
        return new ElevenLabsSettings(null, null, null);
    }
}
