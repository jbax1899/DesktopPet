namespace DesktopPet.App.Memory;

public interface IMemoryStore
{
    IReadOnlyList<MemoryEntry> List();

    MemoryEntry Add(string text);

    void Delete(string id);

    void Clear();
}
