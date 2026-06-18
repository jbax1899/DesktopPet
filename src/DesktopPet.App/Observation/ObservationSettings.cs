namespace DesktopPet.App.Observation;

public enum ScanQuality
{
    Brief,
    Detailed,
    Narrative
}

public enum CommentaryPreset
{
    Quiet,
    Balanced,
    Talkative,
    Custom
}

public sealed record CommentaryTiming(int CooldownMinutes, int CheckInMinutes, int DuplicateWindowMinutes);

public static class ObservationSettingLimits
{
    public const int MinimumCommentThresholdPercent = 0;
    public const int MaximumCommentThresholdPercent = 100;
    public const int MinimumCooldownMinutes = 1;
    public const int MaximumCooldownMinutes = 120;
    public const int MinimumCheckInMinutes = 1;
    public const int MaximumCheckInMinutes = 120;
    public const int MinimumDuplicateWindowMinutes = 1;
    public const int MaximumDuplicateWindowMinutes = 240;
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
    public const int MinimumRecentObservationAgeMinutes = 1;
    public const int MaximumRecentObservationAgeMinutes = 1440;
    public const int MinimumStoredObservationCount = 1;
    public const int MaximumStoredObservationCount = 5000;
    public const int MinimumStoredDecisionCount = 1;
    public const int MaximumStoredDecisionCount = 5000;

    public static CommentaryTiming GetPreset(CommentaryPreset preset) => preset switch
    {
        CommentaryPreset.Talkative => new(2, 3, 10),
        CommentaryPreset.Quiet => new(10, 10, 20),
        _ => new(5, 5, 15)
    };

    public static CommentaryPreset MatchPreset(int cooldownMinutes, int checkInMinutes, int duplicateWindowMinutes)
    {
        foreach (var preset in new[] { CommentaryPreset.Talkative, CommentaryPreset.Balanced, CommentaryPreset.Quiet })
        {
            var timing = GetPreset(preset);
            if (timing.CooldownMinutes == cooldownMinutes
                && timing.CheckInMinutes == checkInMinutes
                && timing.DuplicateWindowMinutes == duplicateWindowMinutes)
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
    int CooldownMinutes,
    int DuplicateWindowMinutes,
    int CheckInMinutes,
    int CommentThresholdPercent,
    double NoveltyWeightPercent,
    double RelevanceWeightPercent,
    double PrivacySafetyWeightPercent,
    double LowInterruptionCostWeightPercent,
    ScanQuality ScanQuality,
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
    int RecentObservationAgeMinutes,
    int StoredObservationCount,
    int StoredAmbientDecisionCount,
    IReadOnlyList<ApplicationObservationRule> ApplicationRules)
{
    public static ObservationSettings Default { get; } = new(
        ObservationEnabled: false,
        AmbientCommentsEnabled: false,
        CaptureScreenshotOnChatSend: true,
        CooldownMinutes: 5,
        DuplicateWindowMinutes: 15,
        CheckInMinutes: 5,
        CommentThresholdPercent: 50,
        NoveltyWeightPercent: 37.5,
        RelevanceWeightPercent: 37.5,
        PrivacySafetyWeightPercent: 12.5,
        LowInterruptionCostWeightPercent: 12.5,
        ScanQuality.Detailed,
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
        RecentObservationAgeMinutes: 30,
        StoredObservationCount: 200,
        StoredAmbientDecisionCount: 100,
        []);

    public double InterestWeightTotal =>
        NoveltyWeightPercent
        + RelevanceWeightPercent
        + PrivacySafetyWeightPercent
        + LowInterruptionCostWeightPercent;
}
