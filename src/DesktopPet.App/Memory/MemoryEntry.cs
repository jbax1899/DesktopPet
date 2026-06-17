namespace DesktopPet.App.Memory;

public sealed record MemoryEntry(
    string Id,
    string Text,
    DateTime CreatedAtUtc);
