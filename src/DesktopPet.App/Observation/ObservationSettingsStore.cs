using System.IO;
using System.Text.Json;

namespace DesktopPet.App.Observation;

public sealed class ObservationSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    public ObservationSettingsStore()
    {
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet");

        _settingsFilePath = Path.Combine(settingsDirectory, "observation-settings.json");
    }

    public ObservationSettings Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return ObservationSettings.Default;
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            var migrated = MigrateOldFormat(json);
            return Normalize(JsonSerializer.Deserialize<ObservationSettings>(migrated, JsonOptions)
                ?? ObservationSettings.Default);
        }
        catch (JsonException)
        {
            return ObservationSettings.Default;
        }
        catch (IOException)
        {
            return ObservationSettings.Default;
        }
    }

    public void Save(ObservationSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsFilePath)
            ?? throw new InvalidOperationException("Observation settings path does not have a directory.");

        Directory.CreateDirectory(directory);
        File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(Normalize(settings), JsonOptions));
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

    private static string MigrateOldFormat(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("CommentaryLevel", out var levelProp))
        {
            return json;
        }

        if (root.TryGetProperty("CooldownMinutes", out _))
        {
            return json;
        }

        var (cooldown, duplicateWindow, checkIn) = levelProp.ValueKind switch
        {
            JsonValueKind.Number => levelProp.GetInt32() switch
            {
                0 => (10, 20, 10),
                2 => (2, 10, 3),
                _ => (5, 15, 5)
            },
            _ => levelProp.GetString() switch
            {
                "Quiet" => (10, 20, 10),
                "Talkative" => (2, 10, 3),
                _ => (5, 15, 5)
            }
        };

        var obj = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions) ?? [];
        obj.Remove("CommentaryLevel");
        obj["CooldownMinutes"] = JsonSerializer.SerializeToElement(cooldown);
        obj["DuplicateWindowMinutes"] = JsonSerializer.SerializeToElement(duplicateWindow);
        obj["CheckInMinutes"] = JsonSerializer.SerializeToElement(checkIn);

        return JsonSerializer.Serialize(obj, JsonOptions);
    }
}
