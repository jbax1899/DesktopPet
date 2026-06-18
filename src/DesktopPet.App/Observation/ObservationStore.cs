using System.IO;
using System.Text.Json;

namespace DesktopPet.App.Observation;

public sealed class ObservationStore
{
    private const int MaximumRecords = 200;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly object _sync = new();

    public ObservationStore()
    {
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet",
            "observations.json");
    }

    public IReadOnlyList<ObservationRecord> List()
    {
        lock (_sync)
        {
            return Load().OrderBy(record => record.CapturedAt).ToArray();
        }
    }

    public void Add(ObservationRecord record)
    {
        lock (_sync)
        {
            var records = Load();
            records.Add(record);
            Save(records.OrderByDescending(item => item.CapturedAt).Take(MaximumRecords).ToArray());
        }
    }

    public void UpdateOutcome(string id, ObservationOutcome outcome, DateTimeOffset? spokenAt)
    {
        lock (_sync)
        {
            var records = Load();
            var index = records.FindIndex(r => r.Id == id);
            if (index >= 0)
            {
                records[index] = records[index] with { Outcome = outcome, SpokenAt = spokenAt };
                Save(records);
            }
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
    }

    private List<ObservationRecord> Load()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ObservationRecord>>(
                File.ReadAllText(_filePath),
                JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return [];
        }
    }

    private void Save(IReadOnlyCollection<ObservationRecord> records)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("Observation store path does not have a directory.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(records, JsonOptions));
    }
}
