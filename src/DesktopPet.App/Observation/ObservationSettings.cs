namespace DesktopPet.App.Observation;

public enum CommentaryPreset
{
    Quiet,
    Balanced,
    Talkative,
    Custom
}

public sealed record CommentaryTiming(int CooldownSeconds, int CheckInSeconds, int DuplicateWindowSeconds);

public static class ObservationSettingLimits
{
    public const int MinimumCommentThresholdPercent = 0;
    public const int MaximumCommentThresholdPercent = 100;
    public const int MinimumCooldownSeconds = 1;
    public const int MaximumCooldownSeconds = 7200;
    public const int MinimumCheckInSeconds = 1;
    public const int MaximumCheckInSeconds = 7200;
    public const int MinimumDuplicateWindowSeconds = 1;
    public const int MaximumDuplicateWindowSeconds = 14400;
    public const int MinimumRecentTypingQuietSeconds = 0;
    public const int MaximumRecentTypingQuietSeconds = 60;
    public const int MinimumPollIntervalSeconds = 1;
    public const int MaximumPollIntervalSeconds = 60;
    public const int MinimumDwellTimeSeconds = 5;
    public const int MaximumDwellTimeSeconds = 300;
    public const int MinimumStructureCooldownSeconds = 1;
    public const int MaximumStructureCooldownSeconds = 300;
    public const int MinimumCaptureDelayMilliseconds = 0;
    public const int MaximumCaptureDelayMilliseconds = 5000;
    public const int MinimumVisionCooldownSeconds = 5;
    public const int MaximumVisionCooldownSeconds = 3600;
    public const int MinimumVisionTimeoutSeconds = 5;
    public const int MaximumVisionTimeoutSeconds = 120;
    public const int MinimumScreenshotWidth = 320;
    public const int MaximumScreenshotWidth = 3840;
    public const int MinimumScreenshotHeight = 180;
    public const int MaximumScreenshotHeight = 2160;
    public const int MinimumObservationContextDepth = 0;
    public const int MaximumObservationContextDepth = 20;
    public const int MinimumCommentTopicLimit = 1;
    public const int MaximumCommentTopicLimit = 10;
    public const int MinimumRecentObservationCount = 1;
    public const int MaximumRecentObservationCount = 500;
    public const int MinimumRecentObservationAgeSeconds = 1;
    public const int MaximumRecentObservationAgeSeconds = 86400;
    public const int MinimumStoredObservationCount = 1;
    public const int MaximumStoredObservationCount = 5000;
    public const int MinimumStoredDecisionCount = 1;
    public const int MaximumStoredDecisionCount = 5000;
    public const int MinimumDetailLevel = 1;
    public const int MaximumDetailLevel = 10;
    public const int MinimumVerbosityLevel = 1;
    public const int MaximumVerbosityLevel = 10;

    public static CommentaryTiming GetPreset(CommentaryPreset preset) => preset switch
    {
        CommentaryPreset.Talkative => new(120, 180, 600),
        CommentaryPreset.Quiet => new(600, 600, 1200),
        _ => new(300, 300, 900)
    };

    public static CommentaryPreset MatchPreset(int cooldownSeconds, int checkInSeconds, int duplicateWindowSeconds)
    {
        foreach (var preset in new[] { CommentaryPreset.Talkative, CommentaryPreset.Balanced, CommentaryPreset.Quiet })
        {
            var timing = GetPreset(preset);
            if (timing.CooldownSeconds == cooldownSeconds
                && timing.CheckInSeconds == checkInSeconds
                && timing.DuplicateWindowSeconds == duplicateWindowSeconds)
            {
                return preset;
            }
        }

        return CommentaryPreset.Custom;
    }
}

public sealed record ApplicationObservationRule(
    string ExecutablePath,
    string DisplayName,
    bool IsDenied = false,
    bool AllowMetadata = false,
    bool AllowStructure = false,
    bool AllowVisual = false);

public sealed record ObservationSettings(
    bool ObservationEnabled,
    bool AmbientCommentsEnabled,
    bool CaptureScreenshotOnChatSend,
    int CooldownSeconds,
    int DuplicateWindowSeconds,
    int CheckInSeconds,
    int CommentThresholdPercent,
    double NoveltyWeightPercent,
    double RelevanceWeightPercent,
    double PrivacySafetyWeightPercent,
    double LowInterruptionCostWeightPercent,
    int VisionDetailLevel,
    int VisionVerbosityLevel,
    int RecentTypingQuietSeconds,
    int PollIntervalSeconds,
    int MinimumDwellTimeSeconds,
    int StructureInspectionCooldownSeconds,
    int ScreenshotCaptureDelayMilliseconds,
    int VisionAnalysisCooldownSeconds,
    int VisionRequestTimeoutSeconds,
    int MaximumScreenshotWidth,
    int MaximumScreenshotHeight,
    int ObservationContextDepth,
    int CommentTopicLimit,
    int RecentObservationCount,
    int RecentObservationAgeSeconds,
    int StoredObservationCount,
    int StoredAmbientDecisionCount,
    IReadOnlyList<ApplicationObservationRule> ApplicationRules)
{
    public static ObservationSettings Default { get; } = new(
        ObservationEnabled: false,
        AmbientCommentsEnabled: false,
        CaptureScreenshotOnChatSend: true,
        CooldownSeconds: 300,
        DuplicateWindowSeconds: 900,
        CheckInSeconds: 300,
        CommentThresholdPercent: 50,
        NoveltyWeightPercent: 37.5,
        RelevanceWeightPercent: 37.5,
        PrivacySafetyWeightPercent: 12.5,
        LowInterruptionCostWeightPercent: 12.5,
        VisionDetailLevel: 5,
        VisionVerbosityLevel: 5,
        RecentTypingQuietSeconds: 8,
        PollIntervalSeconds: 2,
        MinimumDwellTimeSeconds: 15,
        StructureInspectionCooldownSeconds: 10,
        ScreenshotCaptureDelayMilliseconds: 200,
        VisionAnalysisCooldownSeconds: 30,
        VisionRequestTimeoutSeconds: 30,
        MaximumScreenshotWidth: 1280,
        MaximumScreenshotHeight: 720,
        ObservationContextDepth: 5,
        CommentTopicLimit: 2,
        RecentObservationCount: 50,
        RecentObservationAgeSeconds: 1800,
        StoredObservationCount: 200,
        StoredAmbientDecisionCount: 100,
        []);

    public double InterestWeightTotal =>
        NoveltyWeightPercent
        + RelevanceWeightPercent
        + PrivacySafetyWeightPercent
        + LowInterruptionCostWeightPercent;
}
