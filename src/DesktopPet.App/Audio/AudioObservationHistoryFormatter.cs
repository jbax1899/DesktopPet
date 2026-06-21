using System.Text;

namespace DesktopPet.App.Audio;

public static class AudioObservationHistoryFormatter
{
    private const int MaximumHistoryLength = 2400;

    public static string? Format(
        IReadOnlyList<AudioObservation>? observations,
        IReadOnlyList<TranscriptWorkingChunk>? transcripts,
        int contextDepth,
        int verbosityLevel = 5,
        DateTimeOffset? asOf = null)
    {
        if (observations is null || observations.Count == 0 || contextDepth <= 0)
        {
            return null;
        }

        var now = asOf ?? DateTimeOffset.UtcNow;
        var transcriptBySegment = (transcripts ?? [])
            .Where(chunk => chunk.ExpiresAt > now)
            .GroupBy(chunk => chunk.SegmentId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(chunk => chunk.EndedAt).First(),
                StringComparer.Ordinal);
        var builder = new StringBuilder();

        foreach (var observation in observations
                     .OrderByDescending(item => item.CreatedAt)
                     .Take(Math.Clamp(contextDepth, 0, 20)))
        {
            var entry = FormatEntry(observation, transcriptBySegment, now, verbosityLevel);
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            if (builder.Length + entry.Length > MaximumHistoryLength)
            {
                var remaining = MaximumHistoryLength - builder.Length;
                if (remaining > 3)
                {
                    builder.Append(Limit(entry, remaining));
                }

                break;
            }

            builder.Append(entry);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static string FormatEntry(
        AudioObservation observation,
        IReadOnlyDictionary<string, TranscriptWorkingChunk> transcriptBySegment,
        DateTimeOffset now,
        int verbosityLevel)
    {
        var source = observation.Source == AudioSourceKind.Microphone
            ? "microphone"
            : "system audio";
        var age = FormatAge(now - observation.CreatedAt);

        var transcript = transcriptBySegment.TryGetValue(observation.SegmentId, out var chunk)
            ? chunk.Text
            : observation.TranscriptExcerpt;
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return string.Empty;
        }

        var maxTranscriptLength = verbosityLevel switch
        {
            <= 3 => 0,
            <= 6 => 80,
            <= 9 => 160,
            _ => 240
        };

        var builder = new StringBuilder();
        builder.Append($"{age} [{source}]");
        if (maxTranscriptLength > 0)
        {
            builder.Append(": ");
            builder.Append(Limit(CollapseWhitespace(transcript), maxTranscriptLength));
        }
        return builder.ToString();
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes < 1)
        {
            return "just now";
        }

        if (age.TotalHours < 1)
        {
            return $"{Math.Max(1, (int)age.TotalMinutes)}m ago";
        }

        return age.TotalDays < 1
            ? $"{Math.Max(1, (int)age.TotalHours)}h ago"
            : $"{Math.Max(1, (int)age.TotalDays)}d ago";
    }

    private static string CollapseWhitespace(string value) =>
        string.Join(
            " ",
            value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

    private static string Limit(string value, int maximum) =>
        value.Length <= maximum
            ? value
            : maximum <= 3
                ? value[..Math.Max(0, maximum)]
                : string.Concat(value.AsSpan(0, maximum - 3), "...");
}

public sealed class AudioObservationContextProvider
{
    private readonly AudioObservationStore _observationStore;
    private readonly TranscriptWorkingBuffer _transcriptBuffer;
    private readonly Func<AudioContextSettings> _settingsProvider;

    public AudioObservationContextProvider(
        AudioObservationStore observationStore,
        TranscriptWorkingBuffer transcriptBuffer,
        Func<AudioContextSettings> settingsProvider)
    {
        _observationStore = observationStore;
        _transcriptBuffer = transcriptBuffer;
        _settingsProvider = settingsProvider;
    }

    public string? GetCurrentContext()
    {
        try
        {
            var settings = _settingsProvider().Normalize();
            if (!settings.Enabled || !settings.AnalysisEnabled)
            {
                return null;
            }

            return AudioObservationHistoryFormatter.Format(
                _observationStore.List(),
                _transcriptBuffer.List(),
                settings.ContextDepth,
                settings.TranscriptVerbosityLevel);
        }
        catch
        {
            return null;
        }
    }
}
