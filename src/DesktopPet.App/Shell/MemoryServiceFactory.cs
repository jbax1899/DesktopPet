using DesktopPet.App.Memory;

namespace DesktopPet.App.Shell;

public sealed class MemoryServiceFactory
{
    public DesktopPetDatabase Database { get; }
    public IMemoryStore MemoryStore { get; }
    public IChatHistoryStore ChatHistoryStore { get; }
    public ChatAudioStore ChatAudioStore { get; }

    public MemoryServiceFactory()
    {
        Database = new DesktopPetDatabase();
        Database.Initialize();
        MemoryStore = new SqliteMemoryStore(Database);
        ChatHistoryStore = new SqliteChatHistoryStore(Database);
        ChatAudioStore = new ChatAudioStore();
    }

    public void Dispose()
    {
        Database.Dispose();
    }
}
