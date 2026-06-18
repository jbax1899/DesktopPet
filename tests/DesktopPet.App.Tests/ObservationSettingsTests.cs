using DesktopPet.App.Observation;

namespace DesktopPet.App.Tests;

[TestClass]
public sealed class ObservationSettingsTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "DesktopPet.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public void LoadMigratesLegacyVisionSensitivityAndSuppliesNewDefaults()
    {
        var path = Path.Combine(_directory, "observation-settings.json");
        File.WriteAllText(path, """
        {
          "ObservationEnabled": true,
          "AmbientCommentsEnabled": true,
          "CooldownMinutes": 10,
          "DuplicateWindowMinutes": 20,
          "CheckInMinutes": 10,
          "VisionSensitivity": 0,
          "ScanQuality": 1,
          "MinimumDwellTimeSeconds": 25,
          "VisionAnalysisCooldownSeconds": 45,
          "ApplicationRules": []
        }
        """);

        var settings = new ObservationSettingsStore(path).Load();

        Assert.IsTrue(settings.ObservationEnabled);
        Assert.AreEqual(70, settings.CommentThresholdPercent);
        Assert.AreEqual(25, settings.MinimumDwellTimeSeconds);
        Assert.AreEqual(45, settings.VisionAnalysisCooldownSeconds);
        Assert.AreEqual(ObservationSettings.Default.PollIntervalSeconds, settings.PollIntervalSeconds);
        Assert.IsTrue(settings.CaptureScreenshotOnChatSend);
        Assert.AreEqual(100d, settings.InterestWeightTotal, 0.001);
    }

    [TestMethod]
    public void SaveNormalizesAllConfiguredRanges()
    {
        var path = Path.Combine(_directory, "observation-settings.json");
        var store = new ObservationSettingsStore(path);
        store.Save(ObservationSettings.Default with
        {
            CooldownMinutes = -1,
            CommentThresholdPercent = 150,
            PollIntervalSeconds = 0,
            MaximumScreenshotWidth = 99999,
            ObservationContextDepth = -2,
            StoredObservationCount = 0
        });

        var loaded = store.Load();

        Assert.AreEqual(ObservationSettingLimits.MinimumCooldownMinutes, loaded.CooldownMinutes);
        Assert.AreEqual(100, loaded.CommentThresholdPercent);
        Assert.AreEqual(ObservationSettingLimits.MinimumPollIntervalSeconds, loaded.PollIntervalSeconds);
        Assert.AreEqual(ObservationSettingLimits.MaximumScreenshotWidth, loaded.MaximumScreenshotWidth);
        Assert.AreEqual(0, loaded.ObservationContextDepth);
        Assert.AreEqual(ObservationSettingLimits.MinimumStoredObservationCount, loaded.StoredObservationCount);
    }

    [TestMethod]
    public void SaveRoundTripsApplicationRulesAndAdvancedValues()
    {
        var path = Path.Combine(_directory, "observation-settings.json");
        var store = new ObservationSettingsStore(path);
        store.Save(ObservationSettings.Default with
        {
            CaptureScreenshotOnChatSend = false,
            CommentThresholdPercent = 42,
            ObservationContextDepth = 9,
            ApplicationRules =
            [
                new ApplicationObservationRule(
                    Path.Combine(_directory, "sample.exe"),
                    "Sample",
                    AllowMetadata: true,
                    AllowStructure: true,
                    AllowVisual: true)
            ]
        });

        var loaded = store.Load();

        Assert.AreEqual(42, loaded.CommentThresholdPercent);
        Assert.AreEqual(9, loaded.ObservationContextDepth);
        Assert.IsFalse(loaded.CaptureScreenshotOnChatSend);
        Assert.HasCount(1, loaded.ApplicationRules);
        Assert.IsTrue(loaded.ApplicationRules[0].AllowVisual);
    }

    [TestMethod]
    public void PresetsMatchOnlyCanonicalTriples()
    {
        Assert.AreEqual(
            CommentaryPreset.Talkative,
            ObservationSettingLimits.MatchPreset(2, 3, 10));
        Assert.AreEqual(
            CommentaryPreset.Balanced,
            ObservationSettingLimits.MatchPreset(5, 5, 15));
        Assert.AreEqual(
            CommentaryPreset.Quiet,
            ObservationSettingLimits.MatchPreset(10, 10, 20));
        Assert.AreEqual(
            CommentaryPreset.Custom,
            ObservationSettingLimits.MatchPreset(5, 6, 15));
    }

    [TestMethod]
    public void PolicyUsesConfiguredWeightsThresholdAndVisionDuplicateSuppression()
    {
        var path = Path.Combine(_directory, "observation-settings.json");
        var store = new ObservationSettingsStore(path);
        store.Save(ObservationSettings.Default with
        {
            ObservationEnabled = true,
            AmbientCommentsEnabled = true,
            CooldownMinutes = 1,
            DuplicateWindowMinutes = 15,
            CommentThresholdPercent = 60,
            NoveltyWeightPercent = 100,
            RelevanceWeightPercent = 0,
            PrivacySafetyWeightPercent = 0,
            LowInterruptionCostWeightPercent = 0
        });
        var permissions = new ObservationPermissionService(store);
        var policy = new AmbientCommentPolicy(permissions, new TestActivityState());
        var now = DateTimeOffset.UtcNow;
        var change = new DesktopObservationChange(
            DesktopObservationChangeType.CheckIn,
            new ReducedDesktopObservation("app.exe", "App", "Work", now, DesktopContextCapabilities.Metadata),
            "app|checkin|work");
        var candidate = new AmbientCommentCandidate(change, PermissionStillAllowed: true);
        var low = CreateVisionObservation(novelty: 0.59);
        var high = CreateVisionObservation(novelty: 0.60);

        Assert.AreEqual(AmbientDecisionReason.BelowThreshold, policy.Evaluate(candidate, now, low).Reason);
        Assert.IsTrue(policy.Evaluate(candidate, now, high).MaySpeak);

        policy.RecordSpoken(candidate, now);
        var duplicate = policy.Evaluate(candidate, now.AddMinutes(2), high);
        Assert.AreEqual(AmbientDecisionReason.DuplicateTopic, duplicate.Reason);
    }

    [TestMethod]
    public void LoweringObservationRetentionDeletesDiscardedThumbnail()
    {
        var maximum = 3;
        var store = new ObservationStore(_directory, () => maximum);
        var oldThumbnail = Path.Combine(_directory, "old.jpg");
        File.WriteAllText(oldThumbnail, "thumbnail");

        store.Add(CreateRecord("old", DateTimeOffset.UtcNow.AddMinutes(-3), oldThumbnail));
        store.Add(CreateRecord("middle", DateTimeOffset.UtcNow.AddMinutes(-2), null));
        store.Add(CreateRecord("new", DateTimeOffset.UtcNow.AddMinutes(-1), null));

        maximum = 2;
        store.ApplyRetentionLimit();

        Assert.HasCount(2, store.List());
        Assert.IsFalse(File.Exists(oldThumbnail));
    }

    private static VisionObservation CreateVisionObservation(double novelty) => new(
        "Summary",
        null,
        [],
        [],
        novelty,
        0,
        0,
        0,
        60);

    private static ObservationRecord CreateRecord(string id, DateTimeOffset capturedAt, string? thumbnailPath) => new(
        id,
        capturedAt,
        "App",
        "Title",
        "test",
        "test",
        CreateVisionObservation(1),
        1,
        ObservationOutcome.BelowThreshold,
        null,
        thumbnailPath);

    private sealed class TestActivityState : IAmbientActivityState
    {
        public event EventHandler? UserRequestStarted
        {
            add { }
            remove { }
        }
        public bool IsUserRequestActive => false;
        public bool IsSpeechActive => false;
        public DateTimeOffset LastUserInputAt => DateTimeOffset.MinValue;
        public void SetUserRequestActive(bool active) { }
        public void SetSpeechActive(bool active) { }
        public void RecordUserInput() { }
    }
}
