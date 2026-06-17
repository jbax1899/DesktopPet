using System.IO;
using System.Text.Json;

namespace DesktopPet.App.Settings;

public sealed class ProfileSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    public ProfileSettingsStore()
    {
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet");

        _settingsFilePath = Path.Combine(settingsDirectory, "pet-profile-settings.json");
    }

    public ProfileSettings Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return ProfileSettings.Default;
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<ProfileSettings>(json, JsonOptions)
                ?? ProfileSettings.Default;
        }
        catch (JsonException)
        {
            return ProfileSettings.Default;
        }
        catch (IOException)
        {
            return ProfileSettings.Default;
        }
    }

    public void Save(ProfileSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsFilePath)
            ?? throw new InvalidOperationException("Settings file path does not have a directory.");

        Directory.CreateDirectory(directory);
        File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
