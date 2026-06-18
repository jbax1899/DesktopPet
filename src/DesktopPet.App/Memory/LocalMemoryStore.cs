using System.IO;
using System.Text.Json;

namespace DesktopPet.App.Memory;

public sealed class LocalMemoryStore : IMemoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _memoryFilePath;

    public LocalMemoryStore()
    {
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet");

        _memoryFilePath = Path.Combine(settingsDirectory, "memories.json");
    }

    public IReadOnlyList<MemoryEntry> List()
    {
        return LoadAll()
            .OrderBy(memory => memory.CreatedAtUtc)
            .ToList();
    }

    public MemoryEntry Add(string text)
    {
        var trimmedText = text.Trim();
        if (string.IsNullOrWhiteSpace(trimmedText))
        {
            throw new ArgumentException("Memory text cannot be blank.", nameof(text));
        }

        var memory = new MemoryEntry(
            Guid.NewGuid().ToString("N"),
            trimmedText,
            DateTime.UtcNow);
        var memories = LoadAll();
        memories.Add(memory);
        SaveAll(memories);
        return memory;
    }

    public void Delete(string id)
    {
        var memories = LoadAll();
        memories.RemoveAll(memory => memory.Id == id);
        SaveAll(memories);
    }

    public void Clear()
    {
        SaveAll([]);
    }

    private List<MemoryEntry> LoadAll()
    {
        if (!File.Exists(_memoryFilePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_memoryFilePath);
            return JsonSerializer.Deserialize<List<MemoryEntry>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private void SaveAll(IReadOnlyCollection<MemoryEntry> memories)
    {
        var directory = Path.GetDirectoryName(_memoryFilePath)
            ?? throw new InvalidOperationException("Memory file path does not have a directory.");

        Directory.CreateDirectory(directory);
        File.WriteAllText(_memoryFilePath, JsonSerializer.Serialize(memories, JsonOptions));
    }
}
