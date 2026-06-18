using System.IO;
using System.Text.Json;
using DesktopPet.App.Storage;

namespace DesktopPet.App.Observation;

public sealed class ObservationSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly JsonFileStore<ObservationSettings> _settingsFile;

    public ObservationSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet",
            "observation-settings.json"))
    {
    }

    internal ObservationSettingsStore(string settingsFilePath)
    {
        _settingsFile = new JsonFileStore<ObservationSettings>(
            settingsFilePath,
            json => Normalize(JsonSerializer.Deserialize<ObservationSettings>(json, JsonOptions)
                ?? throw new JsonException("Observation settings are empty.")),
            settings => JsonSerializer.Serialize(Normalize(settings), JsonOptions));
    }

    public ObservationSettings Load()
    {
        return _settingsFile.Load(ObservationSettings.Default);
    }

    public void Save(ObservationSettings settings)
    {
        _settingsFile.Save(settings);
    }

    private static ObservationSettings Normalize(ObservationSettings settings)
    {
        var rules = settings.ApplicationRules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.ExecutablePath))
            .Select(rule => rule with
            {
                ExecutablePath = ObservationApplicationIdentity.NormalizePath(rule.ExecutablePath),
                DisplayName = string.IsNullOrWhiteSpace(rule.DisplayName)
                    ? Path.GetFileNameWithoutExtension(rule.ExecutablePath)
                    : rule.DisplayName.Trim()
            })
            .GroupBy(rule => rule.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(rule => rule.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var minimumDwell = settings.MinimumDwellTimeSeconds < 5 ? 15 : settings.MinimumDwellTimeSeconds;
        var visionCooldown = settings.VisionAnalysisCooldownSeconds < 5 ? 30 : settings.VisionAnalysisCooldownSeconds;
        var cooldown = settings.CooldownMinutes < 1 ? 5 : settings.CooldownMinutes;
        var duplicateWindow = settings.DuplicateWindowMinutes < 1 ? 15 : settings.DuplicateWindowMinutes;
        var checkIn = settings.CheckInMinutes < 1 ? 5 : settings.CheckInMinutes;

        return settings with
        {
            ApplicationRules = rules,
            MinimumDwellTimeSeconds = minimumDwell,
            VisionAnalysisCooldownSeconds = visionCooldown,
            CooldownMinutes = cooldown,
            DuplicateWindowMinutes = duplicateWindow,
            CheckInMinutes = checkIn
        };
    }
}
