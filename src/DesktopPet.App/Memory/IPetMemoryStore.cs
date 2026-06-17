namespace DesktopPet.App.Memory;

public interface IPetMemoryStore
{
    IReadOnlyList<PetMemoryEntry> List();

    PetMemoryEntry Add(string text);

    void Delete(string id);

    void Clear();
}
