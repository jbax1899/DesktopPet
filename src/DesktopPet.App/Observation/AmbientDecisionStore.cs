using System.IO;
using System.Text.Json;
using DesktopPet.App.Storage;

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
    private readonly JsonFileStore<List<AmbientDecisionRecord>> _file;
    private readonly object _sync = new();

    public AmbientDecisionStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet",
            "ambient-decisions.json"))
    {
    }

    internal AmbientDecisionStore(string filePath)
    {
        _file = new JsonFileStore<List<AmbientDecisionRecord>>(
            filePath,
            json => JsonSerializer.Deserialize<List<AmbientDecisionRecord>>(json, JsonOptions)
                ?? throw new JsonException("Ambient decisions are empty."),
            records => JsonSerializer.Serialize(records, JsonOptions));
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
            _file.Delete();
        }
    }

    private List<AmbientDecisionRecord> Load()
    {
        return _file.Load([]);
    }

    private void Save(IReadOnlyCollection<AmbientDecisionRecord> records)
    {
        _file.Save(records.ToList());
    }
}
