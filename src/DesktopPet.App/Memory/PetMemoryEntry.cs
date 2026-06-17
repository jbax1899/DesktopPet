namespace DesktopPet.App.Memory;

public sealed record PetMemoryEntry(
    string Id,
    string Text,
    DateTime CreatedAtUtc);
