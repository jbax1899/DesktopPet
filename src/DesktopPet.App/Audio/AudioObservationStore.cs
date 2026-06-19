using System.IO;
using System.Text.Json;
using DesktopPet.App.Storage;

namespace DesktopPet.App.Audio;

public sealed class AudioObservationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly JsonFileStore<List<AudioObservation>> _file;
    private readonly object _sync = new();
    private readonly Func<int> _maximumRecordsProvider;

    public AudioObservationStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopPet",
                "audio-observations.json"))
    {
    }

    internal AudioObservationStore(string filePath, Func<int>? maximumRecordsProvider = null)
    {
        _file = new JsonFileStore<List<AudioObservation>>(
            filePath,
            json => JsonSerializer.Deserialize<List<AudioObservation>>(json, JsonOptions)
                ?? throw new JsonException("Audio observations are empty."),
            records => JsonSerializer.Serialize(records, JsonOptions));
        _maximumRecordsProvider = maximumRecordsProvider
            ?? (() => AudioContextSettings.Default.StoredObservationCount);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<AudioObservation> List()
    {
        lock (_sync)
        {
            return Load().OrderByDescending(item => item.CreatedAt).ToArray();
        }
    }

    public void Add(AudioObservation observation)
    {
        lock (_sync)
        {
            var records = Load();
            records.Add(observation);
            SavePruned(records);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Delete(string id)
    {
        bool changed;
        lock (_sync)
        {
            var records = Load();
            changed = records.RemoveAll(item => item.Id == id) > 0;
            if (changed)
            {
                Save(records);
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    public void Clear()
    {
        lock (_sync)
        {
            _file.Delete();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyRetentionLimit()
    {
        lock (_sync)
        {
            SavePruned(Load());
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<AudioObservation> Load() => _file.Load([]);

    private void Save(IEnumerable<AudioObservation> records) => _file.Save(records.ToList());

    private void SavePruned(IEnumerable<AudioObservation> records)
    {
        Save(records
            .OrderByDescending(item => item.CreatedAt)
            .Take(Math.Max(1, _maximumRecordsProvider())));
    }
}
