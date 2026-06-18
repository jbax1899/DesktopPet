using System.IO;
using System.Text.Json;
using DesktopPet.App.Storage;

namespace DesktopPet.App.Settings;

public sealed class ProfileSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly JsonFileStore<ProfileSettings> _settingsFile;

    public ProfileSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet",
            "pet-profile-settings.json"))
    {
    }

    internal ProfileSettingsStore(string settingsFilePath)
    {
        _settingsFile = new JsonFileStore<ProfileSettings>(
            settingsFilePath,
            json => JsonSerializer.Deserialize<ProfileSettings>(json, JsonOptions)
                ?? throw new JsonException("Profile settings are empty."),
            settings => JsonSerializer.Serialize(settings, JsonOptions));
    }

    public ProfileSettings Load()
    {
        return _settingsFile.Load(ProfileSettings.Default);
    }

    public void Save(ProfileSettings settings)
    {
        _settingsFile.Save(settings);
    }
}
