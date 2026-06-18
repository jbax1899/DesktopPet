using System.IO;
using System.Text.Json;
using DesktopPet.App.Storage;

namespace DesktopPet.App.Observation;

public sealed class ObservationStore
{
    private const int MaximumRecords = 200;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly JsonFileStore<List<ObservationRecord>> _file;
    private readonly string _thumbnailDirectory;
    private readonly object _sync = new();

    public ObservationStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet"))
    {
    }

    internal ObservationStore(string dataDirectory)
    {
        _file = new JsonFileStore<List<ObservationRecord>>(
            Path.Combine(dataDirectory, "observations.json"),
            json => JsonSerializer.Deserialize<List<ObservationRecord>>(json, JsonOptions)
                ?? throw new JsonException("Observations are empty."),
            records => JsonSerializer.Serialize(records, JsonOptions));
        _thumbnailDirectory = Path.Combine(dataDirectory, "thumbnails");
    }

    public string SaveThumbnail(System.Drawing.Bitmap bitmap, string id)
    {
        Directory.CreateDirectory(_thumbnailDirectory);
        var path = Path.Combine(_thumbnailDirectory, $"{id}.jpg");
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Jpeg);
        return path;
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
            _file.Delete();

            if (Directory.Exists(_thumbnailDirectory))
            {
                Directory.Delete(_thumbnailDirectory, recursive: true);
            }
        }
    }

    private List<ObservationRecord> Load()
    {
        return _file.Load([]);
    }

    private void Save(IReadOnlyCollection<ObservationRecord> records)
    {
        _file.Save(records.ToList());
    }
}
