using System.Text;
using System.Globalization;

namespace DesktopPet.App.Memory;

public static class ConversationHistoryFormatter
{
    private const int MaximumTurns = 20;
    private const int MaximumTotalLength = 8_000;
    private const int MaximumTextLength = 600;
    private const int MaximumContextLength = 500;

    public static string? Format(
        IReadOnlyList<ChatHistoryMessage>? messages,
        DateTimeOffset? asOf = null,
        TimeZoneInfo? timeZone = null)
    {
        if (messages is null || messages.Count == 0)
        {
            return null;
        }

        var now = asOf ?? DateTimeOffset.UtcNow;
        var localTimeZone = timeZone ?? TimeZoneInfo.Local;
        var newestTurns = messages
            .OrderByDescending(message => message.CreatedAtUtc)
            .Take(MaximumTurns)
            .Select(message => FormatTurn(message, now, localTimeZone))
            .ToArray();

        var selectedTurns = new List<string>();
        var selectedLength = 0;
        foreach (var turn in newestTurns)
        {
            var separatorLength = selectedTurns.Count == 0 ? 0 : Environment.NewLine.Length;
            if (selectedLength + separatorLength + turn.Length > MaximumTotalLength)
            {
                continue;
            }

            selectedTurns.Add(turn);
            selectedLength += separatorLength + turn.Length;
        }

        selectedTurns.Reverse();
        return selectedTurns.Count == 0
            ? null
            : string.Join(Environment.NewLine, selectedTurns);
    }

    private static string FormatTurn(
        ChatHistoryMessage message,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        var speaker = message.Role == ChatHistoryRole.User
            ? "User"
            : message.Origin == ChatHistoryOrigin.AmbientReply
                ? "Pet (ambient observation)"
                : "Pet";

        var builder = new StringBuilder();
        builder.Append('[');
        builder.Append(FormatRelativeTime(message.CreatedAtUtc, now, timeZone));
        builder.Append(" | ");
        builder.Append(FormatLocalTime(message.CreatedAtUtc, timeZone));
        builder.Append("] ");
        builder.Append(speaker);
        builder.Append(": ");
        builder.Append(Truncate(CollapseWhitespace(message.Text), MaximumTextLength));

        if (!string.IsNullOrWhiteSpace(message.DesktopContext))
        {
            builder.Append(" | Context used: ");
            builder.Append(Truncate(CollapseWhitespace(message.DesktopContext), MaximumContextLength));
        }

        return builder.ToString();
    }

    private static string FormatRelativeTime(
        DateTime createdAtUtc,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        var createdAt = ToUtcDateTimeOffset(createdAtUtc);
        var elapsed = now.ToUniversalTime() - createdAt;
        if (elapsed < TimeSpan.Zero)
        {
            var future = elapsed.Duration();
            if (future < TimeSpan.FromMinutes(1))
            {
                return "just now";
            }

            if (future < TimeSpan.FromHours(1))
            {
                return $"in {Pluralize((int)future.TotalMinutes, "minute")}";
            }

            if (future < TimeSpan.FromDays(1))
            {
                return $"in {Pluralize((int)future.TotalHours, "hour")}";
            }

            return $"in {Pluralize((int)future.TotalDays, "day")}";
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{Pluralize((int)elapsed.TotalMinutes, "minute")} ago";
        }

        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var localCreatedAt = TimeZoneInfo.ConvertTime(createdAt, timeZone);
        if (localCreatedAt.Date == localNow.Date)
        {
            return $"{Pluralize((int)elapsed.TotalHours, "hour")} ago";
        }

        if (localCreatedAt.Date == localNow.Date.AddDays(-1))
        {
            return $"yesterday at {localCreatedAt.ToString("h:mm tt", CultureInfo.InvariantCulture)}";
        }

        return $"{Pluralize(Math.Max(1, (localNow.Date - localCreatedAt.Date).Days), "day")} ago";
    }

    private static string FormatLocalTime(DateTime createdAtUtc, TimeZoneInfo timeZone)
    {
        var localCreatedAt = TimeZoneInfo.ConvertTime(ToUtcDateTimeOffset(createdAtUtc), timeZone);
        return string.Concat(
            localCreatedAt.ToString("MMMM d, yyyy 'at' h:mm tt", CultureInfo.InvariantCulture),
            " ",
            TemporalContextFormatter.FormatUtcOffset(localCreatedAt.Offset));
    }

    private static DateTimeOffset ToUtcDateTimeOffset(DateTime value)
    {
        var utcValue = value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return new DateTimeOffset(utcValue);
    }

    private static string Pluralize(int value, string unit)
    {
        return $"{value} {unit}{(value == 1 ? string.Empty : "s")}";
    }

    private static string CollapseWhitespace(string value)
    {
        return string.Join(
            " ",
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string Truncate(string value, int maximumLength)
    {
        return value.Length <= maximumLength
            ? value
            : string.Concat(value.AsSpan(0, maximumLength - 3), "...");
    }
}
