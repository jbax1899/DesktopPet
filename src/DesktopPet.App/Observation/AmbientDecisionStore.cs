using System.IO;
using System.Text.Json;

namespace DesktopPet.App.Observation;

public sealed record AmbientDecisionRecord(
    string Observation,
    bool Spoke,
    AmbientDecisionReason Reason,
    DateTimeOffset CreatedAt);

public sealed class AmbientDecisionStore
{
    private const int MaximumRecords = 100;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly object _sync = new();

    public AmbientDecisionStore()
    {
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet",
            "ambient-decisions.json");
    }

    public IReadOnlyList<AmbientDecisionRecord> List()
    {
        lock (_sync)
        {
            return Load().OrderByDescending(record => record.CreatedAt).ToArray();
        }
    }

    public void Add(DesktopObservationChange change, bool spoke, AmbientDecisionReason reason)
    {
        var description = $"{change.Observation.ApplicationName}: {change.Observation.ActivityDescription}";
        if (!string.IsNullOrWhiteSpace(change.Observation.StructuralDescription))
        {
            description = $"{description}. {change.Observation.StructuralDescription}";
        }

        var record = new AmbientDecisionRecord(description, spoke, reason, DateTimeOffset.UtcNow);
        lock (_sync)
        {
            var records = Load();
            records.Add(record);
            Save(records.OrderByDescending(item => item.CreatedAt).Take(MaximumRecords).ToArray());
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

    private List<AmbientDecisionRecord> Load()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<AmbientDecisionRecord>>(
                File.ReadAllText(_filePath),
                JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return [];
        }
    }

    private void Save(IReadOnlyCollection<AmbientDecisionRecord> records)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("Ambient decision path does not have a directory.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(records, JsonOptions));
    }
}
