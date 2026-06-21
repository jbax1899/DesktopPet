using System.IO;
using System.Text.Json;
using DesktopPet.App.Storage;

namespace DesktopPet.App.Audio;

public sealed class AudioContextSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly JsonFileStore<AudioContextSettings> _settingsFile;

    public AudioContextSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet",
            "audio-context-settings.json"))
    {
    }

    internal AudioContextSettingsStore(string settingsFilePath)
    {
        _settingsFile = new JsonFileStore<AudioContextSettings>(
            settingsFilePath,
            Deserialize,
            settings => JsonSerializer.Serialize(settings, JsonOptions));
    }

    public AudioContextSettings Load() => _settingsFile.Load(AudioContextSettings.Default);

    public void Save(AudioContextSettings settings) => _settingsFile.Save(settings.Normalize());

    private static AudioContextSettings Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var defaults = AudioContextSettings.Default;
        return new AudioContextSettings(
            Read(root, nameof(AudioContextSettings.Enabled), defaults.Enabled),
            Read(root, nameof(AudioContextSettings.MicrophoneEnabled), defaults.MicrophoneEnabled),
            Read(root, nameof(AudioContextSettings.SystemAudioEnabled), defaults.SystemAudioEnabled),
            Read(root, nameof(AudioContextSettings.AnalysisEnabled), defaults.AnalysisEnabled),
            Read(root, nameof(AudioContextSettings.PersistMicrophoneTranscriptExcerpt), defaults.PersistMicrophoneTranscriptExcerpt),
            Read(root, nameof(AudioContextSettings.PersistSystemAudioTranscriptExcerpt), defaults.PersistSystemAudioTranscriptExcerpt),
            ReadInt(root, nameof(AudioContextSettings.ContextDepth), defaults.ContextDepth),
            ReadDurationSeconds(
                root,
                nameof(AudioContextSettings.TranscriptRetentionSeconds),
                "TranscriptRetentionMinutes",
                defaults.TranscriptRetentionSeconds),
            ReadInt(root, nameof(AudioContextSettings.StoredObservationCount), defaults.StoredObservationCount),
            ReadDouble(root, nameof(AudioContextSettings.MinimumAnalysisConfidence), defaults.MinimumAnalysisConfidence),
            ReadInt(root, nameof(AudioContextSettings.AnalysisTimeoutSeconds), defaults.AnalysisTimeoutSeconds),
            ReadInt(root, nameof(AudioContextSettings.TranscriptVerbosityLevel), defaults.TranscriptVerbosityLevel),
            ReadInt(root, nameof(AudioContextSettings.MaximumSegmentDurationSeconds), defaults.MaximumSegmentDurationSeconds))
            .Normalize();
    }

    private static bool Read(JsonElement root, string name, bool fallback)
    {
        return root.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : fallback;
    }

    private static int ReadInt(JsonElement root, string name, int fallback)
    {
        return root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : fallback;
    }

    private static int ReadDurationSeconds(
        JsonElement root,
        string secondsName,
        string legacyMinutesName,
        int fallback)
    {
        if (root.TryGetProperty(secondsName, out var secondsValue)
            && secondsValue.TryGetInt32(out var seconds))
        {
            return seconds;
        }

        if (root.TryGetProperty(legacyMinutesName, out var minutesValue)
            && minutesValue.TryGetInt32(out var minutes))
        {
            return (int)Math.Clamp((long)minutes * 60, int.MinValue, int.MaxValue);
        }

        return fallback;
    }

    private static double ReadDouble(JsonElement root, string name, double fallback)
    {
        return root.TryGetProperty(name, out var value) && value.TryGetDouble(out var result)
            ? result
            : fallback;
    }
}
