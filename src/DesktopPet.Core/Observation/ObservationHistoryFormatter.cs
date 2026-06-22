using System.Text;

namespace DesktopPet.App.Observation;

public static class ObservationHistoryFormatter
{
    private const int MaximumHistoryLength = 1500;
    private const int MaximumEntryLength = 200;

    public static string? Format(IReadOnlyList<ObservationRecord>? records, DateTimeOffset? asOf = null)
    {
        if (records is null || records.Count == 0)
        {
            return null;
        }

        var now = asOf ?? DateTimeOffset.UtcNow;
        var sb = new StringBuilder();

        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            var ago = now - record.CapturedAt;
            var timeAgo = FormatTimeAgo(ago);

            var entry = new StringBuilder();
            entry.Append($"{timeAgo}: {record.Application}");

            if (!string.IsNullOrWhiteSpace(record.WindowTitle))
            {
                entry.Append($" - {Truncate(record.WindowTitle, 80)}");
            }

            if (!string.IsNullOrWhiteSpace(record.Analysis?.Summary))
            {
                entry.Append($". {Truncate(record.Analysis.Summary, 100)}");
            }

            if (record.Outcome == ObservationOutcome.Spoken)
            {
                entry.Append(" [spoken]");
            }

            var entryText = Truncate(entry.ToString(), MaximumEntryLength);
            if (sb.Length > 0)
            {
                sb.Append("; ");
            }

            sb.Append(entryText);

            if (sb.Length >= MaximumHistoryLength)
            {
                break;
            }
        }

        var result = sb.ToString();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static string FormatTimeAgo(TimeSpan ago)
    {
        if (ago.TotalMinutes < 1)
        {
            return "just now";
        }

        if (ago.TotalMinutes < 60)
        {
            var minutes = (int)ago.TotalMinutes;
            return $"{minutes}m ago";
        }

        if (ago.TotalHours < 24)
        {
            var hours = (int)ago.TotalHours;
            return $"{hours}h ago";
        }

        var days = (int)ago.TotalDays;
        return $"{days}d ago";
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return string.Concat(value.AsSpan(0, maxLength - 3), "...");
    }
}
