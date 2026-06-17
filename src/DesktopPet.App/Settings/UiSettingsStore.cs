using System.IO;
using System.Text.Json;

namespace DesktopPet.App.Settings;

public sealed class UiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    public UiSettingsStore()
    {
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet");

        _settingsFilePath = Path.Combine(settingsDirectory, "ui-settings.json");
    }

    public UiSettings Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return UiSettings.Default;
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<UiSettings>(json, JsonOptions) ?? UiSettings.Default;
            return settings.ChatShortcut.IsValid() ? settings : UiSettings.Default;
        }
        catch (JsonException)
        {
            return UiSettings.Default;
        }
        catch (IOException)
        {
            return UiSettings.Default;
        }
    }

    public void Save(UiSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsFilePath)
            ?? throw new InvalidOperationException("Settings file path does not have a directory.");

        Directory.CreateDirectory(directory);
        File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
