using DesktopPet.App.Memory;
using Microsoft.Data.Sqlite;

namespace DesktopPet.App.Tests;

[TestClass]
public sealed class MemoryStoreTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "DesktopPet.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public void ChatHistoryStoreDeletesSelectedMessageAndClearsRemainingMessages()
    {
        var database = new DesktopPetDatabase(_directory);
        database.Initialize();
        var store = new SqliteChatHistoryStore(database);
        var changed = 0;
        store.Changed += (_, _) => changed++;
        var first = store.Add(ChatHistoryRole.User, "First");
        store.Add(ChatHistoryRole.Bot, "Second");

        store.Delete(first.Id);

        var remaining = store.List();
        Assert.HasCount(1, remaining);
        Assert.AreEqual("Second", remaining[0].Text);

        store.Clear();

        Assert.IsEmpty(store.List());
        Assert.AreEqual(4, changed);
    }
}
