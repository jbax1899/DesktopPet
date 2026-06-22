using System.IO;
using System.Text.Json;
using DesktopPet.App.Storage;

namespace DesktopPet.App.Observation;

public sealed class ObservationSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
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
            Deserialize,
            settings => JsonSerializer.Serialize(Normalize(settings), JsonOptions));
    }

    public ObservationSettings Load() => _settingsFile.Load(ObservationSettings.Default);

    public void Save(ObservationSettings settings) => _settingsFile.Save(settings);

    public static ObservationSettings Normalize(ObservationSettings settings)
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

        return settings with
        {
            CooldownSeconds = Math.Clamp(settings.CooldownSeconds, ObservationSettingLimits.MinimumCooldownSeconds, ObservationSettingLimits.MaximumCooldownSeconds),
            DuplicateWindowSeconds = Math.Clamp(settings.DuplicateWindowSeconds, ObservationSettingLimits.MinimumDuplicateWindowSeconds, ObservationSettingLimits.MaximumDuplicateWindowSeconds),
            CheckInSeconds = Math.Clamp(settings.CheckInSeconds, ObservationSettingLimits.MinimumCheckInSeconds, ObservationSettingLimits.MaximumCheckInSeconds),
            CommentThresholdPercent = Math.Clamp(settings.CommentThresholdPercent, ObservationSettingLimits.MinimumCommentThresholdPercent, ObservationSettingLimits.MaximumCommentThresholdPercent),
            NoveltyWeightPercent = Math.Clamp(settings.NoveltyWeightPercent, 0, 100),
            RelevanceWeightPercent = Math.Clamp(settings.RelevanceWeightPercent, 0, 100),
            PrivacySafetyWeightPercent = Math.Clamp(settings.PrivacySafetyWeightPercent, 0, 100),
            LowInterruptionCostWeightPercent = Math.Clamp(settings.LowInterruptionCostWeightPercent, 0, 100),
            VisionDetailLevel = Math.Clamp(settings.VisionDetailLevel, ObservationSettingLimits.MinimumDetailLevel, ObservationSettingLimits.MaximumDetailLevel),
            VisionVerbosityLevel = Math.Clamp(settings.VisionVerbosityLevel, ObservationSettingLimits.MinimumVerbosityLevel, ObservationSettingLimits.MaximumVerbosityLevel),
            RecentTypingQuietSeconds = Math.Clamp(settings.RecentTypingQuietSeconds, ObservationSettingLimits.MinimumRecentTypingQuietSeconds, ObservationSettingLimits.MaximumRecentTypingQuietSeconds),
            PollIntervalSeconds = Math.Clamp(settings.PollIntervalSeconds, ObservationSettingLimits.MinimumPollIntervalSeconds, ObservationSettingLimits.MaximumPollIntervalSeconds),
            MinimumDwellTimeSeconds = Math.Clamp(settings.MinimumDwellTimeSeconds, ObservationSettingLimits.MinimumDwellTimeSeconds, ObservationSettingLimits.MaximumDwellTimeSeconds),
            StructureInspectionCooldownSeconds = Math.Clamp(settings.StructureInspectionCooldownSeconds, ObservationSettingLimits.MinimumStructureCooldownSeconds, ObservationSettingLimits.MaximumStructureCooldownSeconds),
            ScreenshotCaptureDelayMilliseconds = Math.Clamp(settings.ScreenshotCaptureDelayMilliseconds, ObservationSettingLimits.MinimumCaptureDelayMilliseconds, ObservationSettingLimits.MaximumCaptureDelayMilliseconds),
            VisionAnalysisCooldownSeconds = Math.Clamp(settings.VisionAnalysisCooldownSeconds, ObservationSettingLimits.MinimumVisionCooldownSeconds, ObservationSettingLimits.MaximumVisionCooldownSeconds),
            VisionRequestTimeoutSeconds = Math.Clamp(settings.VisionRequestTimeoutSeconds, ObservationSettingLimits.MinimumVisionTimeoutSeconds, ObservationSettingLimits.MaximumVisionTimeoutSeconds),
            MaximumScreenshotWidth = Math.Clamp(settings.MaximumScreenshotWidth, ObservationSettingLimits.MinimumScreenshotWidth, ObservationSettingLimits.MaximumScreenshotWidth),
            MaximumScreenshotHeight = Math.Clamp(settings.MaximumScreenshotHeight, ObservationSettingLimits.MinimumScreenshotHeight, ObservationSettingLimits.MaximumScreenshotHeight),
            ObservationContextDepth = Math.Clamp(settings.ObservationContextDepth, ObservationSettingLimits.MinimumObservationContextDepth, ObservationSettingLimits.MaximumObservationContextDepth),
            CommentTopicLimit = Math.Clamp(settings.CommentTopicLimit, ObservationSettingLimits.MinimumCommentTopicLimit, ObservationSettingLimits.MaximumCommentTopicLimit),
            RecentObservationCount = Math.Clamp(settings.RecentObservationCount, ObservationSettingLimits.MinimumRecentObservationCount, ObservationSettingLimits.MaximumRecentObservationCount),
            RecentObservationAgeSeconds = Math.Clamp(settings.RecentObservationAgeSeconds, ObservationSettingLimits.MinimumRecentObservationAgeSeconds, ObservationSettingLimits.MaximumRecentObservationAgeSeconds),
            StoredObservationCount = Math.Clamp(settings.StoredObservationCount, ObservationSettingLimits.MinimumStoredObservationCount, ObservationSettingLimits.MaximumStoredObservationCount),
            StoredAmbientDecisionCount = Math.Clamp(settings.StoredAmbientDecisionCount, ObservationSettingLimits.MinimumStoredDecisionCount, ObservationSettingLimits.MaximumStoredDecisionCount),
            ApplicationRules = rules
        };
    }

    private static ObservationSettings Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var defaults = ObservationSettings.Default;
        var rules = Read(root, nameof(ObservationSettings.ApplicationRules), defaults.ApplicationRules);
        var threshold = root.TryGetProperty(nameof(ObservationSettings.CommentThresholdPercent), out var thresholdElement)
            ? thresholdElement.GetInt32()
            : MigrateVisionSensitivity(root);

        return Normalize(new ObservationSettings(
            Read(root, nameof(ObservationSettings.ObservationEnabled), defaults.ObservationEnabled),
            Read(root, nameof(ObservationSettings.AmbientCommentsEnabled), defaults.AmbientCommentsEnabled),
            Read(root, nameof(ObservationSettings.CaptureScreenshotOnChatSend), defaults.CaptureScreenshotOnChatSend),
            ReadDurationSeconds(
                root,
                nameof(ObservationSettings.CooldownSeconds),
                "CooldownMinutes",
                defaults.CooldownSeconds),
            ReadDurationSeconds(
                root,
                nameof(ObservationSettings.DuplicateWindowSeconds),
                "DuplicateWindowMinutes",
                defaults.DuplicateWindowSeconds),
            ReadDurationSeconds(
                root,
                nameof(ObservationSettings.CheckInSeconds),
                "CheckInMinutes",
                defaults.CheckInSeconds),
            threshold,
            Read(root, nameof(ObservationSettings.NoveltyWeightPercent), defaults.NoveltyWeightPercent),
            Read(root, nameof(ObservationSettings.RelevanceWeightPercent), defaults.RelevanceWeightPercent),
            Read(root, nameof(ObservationSettings.PrivacySafetyWeightPercent), defaults.PrivacySafetyWeightPercent),
            Read(root, nameof(ObservationSettings.LowInterruptionCostWeightPercent), defaults.LowInterruptionCostWeightPercent),
            MigrateVisionDetailLevel(root, defaults.VisionDetailLevel),
            MigrateVisionVerbosityLevel(root, defaults.VisionVerbosityLevel),
            Read(root, nameof(ObservationSettings.RecentTypingQuietSeconds), defaults.RecentTypingQuietSeconds),
            Read(root, nameof(ObservationSettings.PollIntervalSeconds), defaults.PollIntervalSeconds),
            Read(root, nameof(ObservationSettings.MinimumDwellTimeSeconds), defaults.MinimumDwellTimeSeconds),
            Read(root, nameof(ObservationSettings.StructureInspectionCooldownSeconds), defaults.StructureInspectionCooldownSeconds),
            Read(root, nameof(ObservationSettings.ScreenshotCaptureDelayMilliseconds), defaults.ScreenshotCaptureDelayMilliseconds),
            Read(root, nameof(ObservationSettings.VisionAnalysisCooldownSeconds), defaults.VisionAnalysisCooldownSeconds),
            Read(root, nameof(ObservationSettings.VisionRequestTimeoutSeconds), defaults.VisionRequestTimeoutSeconds),
            Read(root, nameof(ObservationSettings.MaximumScreenshotWidth), defaults.MaximumScreenshotWidth),
            Read(root, nameof(ObservationSettings.MaximumScreenshotHeight), defaults.MaximumScreenshotHeight),
            Read(root, nameof(ObservationSettings.ObservationContextDepth), defaults.ObservationContextDepth),
            Read(root, nameof(ObservationSettings.CommentTopicLimit), defaults.CommentTopicLimit),
            Read(root, nameof(ObservationSettings.RecentObservationCount), defaults.RecentObservationCount),
            ReadDurationSeconds(
                root,
                nameof(ObservationSettings.RecentObservationAgeSeconds),
                "RecentObservationAgeMinutes",
                defaults.RecentObservationAgeSeconds),
            Read(root, nameof(ObservationSettings.StoredObservationCount), defaults.StoredObservationCount),
            Read(root, nameof(ObservationSettings.StoredAmbientDecisionCount), defaults.StoredAmbientDecisionCount),
            rules));
    }

    private static int MigrateVisionSensitivity(JsonElement root)
    {
        if (!root.TryGetProperty("VisionSensitivity", out var value))
        {
            return ObservationSettings.Default.CommentThresholdPercent;
        }

        var name = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetInt32() switch
        {
            0 => "Low",
            2 => "High",
            _ => "Medium"
        };
        return name switch
        {
            "Low" => 70,
            "High" => 30,
            _ => 50
        };
    }

    private static int MigrateVisionDetailLevel(JsonElement root, int fallback)
    {
        if (root.TryGetProperty(nameof(ObservationSettings.VisionDetailLevel), out var value)
            && value.TryGetInt32(out var result))
        {
            return result;
        }

        if (root.TryGetProperty("ScanQuality", out var scanQuality))
        {
            var name = scanQuality.ValueKind == JsonValueKind.String
                ? scanQuality.GetString()
                : null;
            return name switch
            {
                "Brief" => 1,
                "Narrative" => 10,
                _ => 5
            };
        }

        return fallback;
    }

    private static int MigrateVisionVerbosityLevel(JsonElement root, int fallback)
    {
        if (root.TryGetProperty(nameof(ObservationSettings.VisionVerbosityLevel), out var value)
            && value.TryGetInt32(out var result))
        {
            return result;
        }

        if (root.TryGetProperty("ScanQuality", out var scanQuality))
        {
            var name = scanQuality.ValueKind == JsonValueKind.String
                ? scanQuality.GetString()
                : null;
            return name switch
            {
                "Brief" => 1,
                "Narrative" => 10,
                _ => 5
            };
        }

        return fallback;
    }

    private static T Read<T>(JsonElement root, string name, T fallback)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        return value.Deserialize<T>(JsonOptions) ?? fallback;
    }

    private static int ReadDurationSeconds(
        JsonElement root,
        string secondsName,
        string legacyMinutesName,
        int fallback)
    {
        if (root.TryGetProperty(secondsName, out var secondsValue)
            && secondsValue.TryGetInt32(out var seconds))
        {
            return seconds;
        }

        if (root.TryGetProperty(legacyMinutesName, out var minutesValue)
            && minutesValue.TryGetInt32(out var minutes))
        {
            return (int)Math.Clamp((long)minutes * 60, int.MinValue, int.MaxValue);
        }

        return fallback;
    }
}
