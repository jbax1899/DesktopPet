using System.IO;
using System.Text.Json;
using DesktopPet.App.Storage;

namespace DesktopPet.App.Observation;

public sealed class ObservationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly JsonFileStore<List<ObservationRecord>> _file;
    private readonly string _thumbnailDirectory;
    private readonly object _sync = new();
    private readonly Func<int> _maximumRecordsProvider;
    private List<ObservationRecord>? _cache;

    public ObservationStore()
        : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopPet"),
            () => ObservationSettings.Default.StoredObservationCount)
    {
    }

    internal ObservationStore(string dataDirectory, Func<int>? maximumRecordsProvider = null)
    {
        _file = new JsonFileStore<List<ObservationRecord>>(
            Path.Combine(dataDirectory, "observations.json"),
            json => JsonSerializer.Deserialize<List<ObservationRecord>>(json, JsonOptions)
                ?? throw new JsonException("Observations are empty."),
            records => JsonSerializer.Serialize(records, JsonOptions));
        _thumbnailDirectory = Path.Combine(dataDirectory, "thumbnails");
        _maximumRecordsProvider = maximumRecordsProvider
            ?? (() => ObservationSettings.Default.StoredObservationCount);
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

    public IReadOnlyList<ObservationRecord> GetRecent(int count)
    {
        lock (_sync)
        {
            return Load()
                .OrderByDescending(record => record.CapturedAt)
                .Take(count)
                .Reverse()
                .ToArray();
        }
    }

    public void Add(ObservationRecord record)
    {
        lock (_sync)
        {
            var records = Load();
            records.Add(record);
            SavePruned(records, _maximumRecordsProvider());
            InvalidateCache();
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
                InvalidateCache();
            }
        }
    }

    public bool Delete(string id)
    {
        lock (_sync)
        {
            var records = Load();
            var removed = records.FirstOrDefault(record => record.Id == id);
            if (removed is null)
            {
                return false;
            }

            records.Remove(removed);
            Save(records);
            InvalidateCache();
            DeleteThumbnail(removed.ThumbnailPath);
            return true;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _file.Delete();
            InvalidateCache();

            if (Directory.Exists(_thumbnailDirectory))
            {
                Directory.Delete(_thumbnailDirectory, recursive: true);
            }
        }
    }

    public void ApplyRetentionLimit()
    {
        lock (_sync)
        {
            SavePruned(Load(), _maximumRecordsProvider());
            InvalidateCache();
        }
    }

    private List<ObservationRecord> Load()
    {
        return _cache ??= _file.Load([]);
    }

    private void InvalidateCache()
    {
        _cache = null;
    }

    private void Save(IReadOnlyCollection<ObservationRecord> records)
    {
        _file.Save(records.ToList());
    }

    private void SavePruned(IEnumerable<ObservationRecord> records, int maximumRecords)
    {
        var ordered = records.OrderByDescending(item => item.CapturedAt).ToList();
        for (var i = maximumRecords; i < ordered.Count; i++)
        {
            DeleteThumbnail(ordered[i].ThumbnailPath);
        }

        if (ordered.Count > maximumRecords)
        {
            ordered.RemoveRange(maximumRecords, ordered.Count - maximumRecords);
        }

        _file.Save(ordered);
    }

    private static void DeleteThumbnail(string? thumbnailPath)
    {
        if (!string.IsNullOrWhiteSpace(thumbnailPath) && File.Exists(thumbnailPath))
        {
            File.Delete(thumbnailPath);
        }
    }
}
