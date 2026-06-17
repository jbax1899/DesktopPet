using System.IO;
using System.Text.Json;

namespace DesktopPet.App.Settings;

public sealed class PetProfileSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    public PetProfileSettingsStore()
    {
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet");

        _settingsFilePath = Path.Combine(settingsDirectory, "pet-profile-settings.json");
    }

    public PetProfileSettings Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return PetProfileSettings.Default;
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<PetProfileSettings>(json, JsonOptions)
                ?? PetProfileSettings.Default;
        }
        catch (JsonException)
        {
            return PetProfileSettings.Default;
        }
        catch (IOException)
        {
            return PetProfileSettings.Default;
        }
    }

    public void Save(PetProfileSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsFilePath)
            ?? throw new InvalidOperationException("Settings file path does not have a directory.");

        Directory.CreateDirectory(directory);
        File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
