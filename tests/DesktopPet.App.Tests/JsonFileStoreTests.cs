using System.Text.Json;
using DesktopPet.App.Settings;
using DesktopPet.App.Storage;

namespace DesktopPet.App.Tests;

[TestClass]
public sealed class JsonFileStoreTests
{
    private string _directory = null!;
    private string _filePath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "DesktopPet.Tests", Guid.NewGuid().ToString("N"));
        _filePath = Path.Combine(_directory, "settings.json");
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
    public void FirstSaveCreatesPrimaryAndSecondSaveKeepsBackup()
    {
        var store = CreateStore();

        store.Save(new TestSettings("first"));
        store.Save(new TestSettings("second"));

        Assert.AreEqual("second", store.Load(TestSettings.Empty).Value);
        Assert.AreEqual(
            "first",
            JsonSerializer.Deserialize<TestSettings>(File.ReadAllText($"{_filePath}.bak"))!.Value);
    }

    [TestMethod]
    public void LoadFallsBackToBackupWithoutChangingFiles()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_filePath, "{broken");
        File.WriteAllText($"{_filePath}.bak", JsonSerializer.Serialize(new TestSettings("backup")));
        var primaryBefore = File.ReadAllText(_filePath);

        var loaded = CreateStore().Load(TestSettings.Empty);

        Assert.AreEqual("backup", loaded.Value);
        Assert.AreEqual(primaryBefore, File.ReadAllText(_filePath));
        Assert.AreEqual(0, Directory.GetFiles(_directory, "*.corrupt").Length);
    }

    [TestMethod]
    public void SavePreservesMalformedPrimaryAndLeavesBackupUntouched()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_filePath, "{broken");
        File.WriteAllText($"{_filePath}.bak", JsonSerializer.Serialize(new TestSettings("backup")));

        CreateStore().Save(new TestSettings("replacement"));

        Assert.AreEqual("replacement", CreateStore().Load(TestSettings.Empty).Value);
        Assert.AreEqual(
            "backup",
            JsonSerializer.Deserialize<TestSettings>(File.ReadAllText($"{_filePath}.bak"))!.Value);
        var corruptPaths = Directory.GetFiles(_directory, "*.corrupt");
        Assert.HasCount(1, corruptPaths);
        Assert.AreEqual("{broken", File.ReadAllText(corruptPaths[0]));
    }

    [TestMethod]
    public void FailedSaveRemovesTemporaryFile()
    {
        Directory.CreateDirectory(_filePath);
        var store = CreateStore();

        Assert.ThrowsExactly<IOException>(() => store.Save(new TestSettings("value")));

        Assert.AreEqual(0, Directory.GetFiles(_directory, "*.tmp").Length);
    }

    [TestMethod]
    public void ConcurrentStoreSavesLeaveValidJson()
    {
        var store = new ProfileSettingsStore(_filePath);

        Parallel.For(
            0,
            20,
            index => store.Save(new ProfileSettings($"User {index}", $"Pet {index}")));

        var loaded = store.Load();
        Assert.IsNotNull(loaded.UserName);
        Assert.IsNotNull(loaded.Nickname);
        Assert.IsNotNull(JsonSerializer.Deserialize<ProfileSettings>(File.ReadAllText(_filePath)));
    }

    [TestMethod]
    public void DeleteRemovesPrimaryBackupCorruptAndTemporaryCopies()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_filePath, "{}");
        File.WriteAllText($"{_filePath}.bak", "{}");
        File.WriteAllText($"{_filePath}.20260101000000000.test.corrupt", "{}");
        File.WriteAllText($"{_filePath}.test.tmp", "{}");

        CreateStore().Delete();

        Assert.IsFalse(File.Exists(_filePath));
        Assert.IsFalse(File.Exists($"{_filePath}.bak"));
        Assert.AreEqual(0, Directory.GetFiles(_directory).Length);
    }

    [TestMethod]
    public void DeleteReportsFailureAndLeavesPrimaryWhenBackupCannotBeDeleted()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_filePath, "{}");
        File.WriteAllText($"{_filePath}.bak", "{}");

        using var lockedBackup = new FileStream(
            $"{_filePath}.bak",
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        Assert.ThrowsExactly<IOException>(() => CreateStore().Delete());
        Assert.IsTrue(File.Exists(_filePath));
    }

    private JsonFileStore<TestSettings> CreateStore()
    {
        return new JsonFileStore<TestSettings>(
            _filePath,
            json => JsonSerializer.Deserialize<TestSettings>(json)
                ?? throw new JsonException("Test settings are empty."),
            settings => JsonSerializer.Serialize(settings));
    }

    private sealed record TestSettings(string Value)
    {
        public static TestSettings Empty { get; } = new(string.Empty);
    }
}
