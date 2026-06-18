using System.Text;
using System.Globalization;

namespace DesktopPet.App.Memory;

public static class ConversationHistoryFormatter
{
    private const int MaximumTextLength = 600;
    private const int MaximumContextLength = 500;
    private const int MaximumConfiguredMessageCount = 50;

    public static string? Format(
        IReadOnlyList<ChatHistoryMessage>? messages,
        DateTimeOffset? asOf = null,
        TimeZoneInfo? timeZone = null,
        int regularMessageCount = 14,
        int ambientMessageCount = 6)
    {
        if (messages is null
            || messages.Count == 0
            || (regularMessageCount <= 0 && ambientMessageCount <= 0))
        {
            return null;
        }

        var now = asOf ?? DateTimeOffset.UtcNow;
        var localTimeZone = timeZone ?? TimeZoneInfo.Local;
        var selectedTurns = SelectTurns(
            messages,
            Math.Clamp(regularMessageCount, 0, MaximumConfiguredMessageCount),
            Math.Clamp(ambientMessageCount, 0, MaximumConfiguredMessageCount));

        return selectedTurns.Count == 0
            ? null
            : string.Join(
                Environment.NewLine,
                selectedTurns.Select(turn => FormatTurn(turn, now, localTimeZone)));
    }

    private static string FormatTurn(
        SelectedHistoryTurn turn,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        var message = turn.Message;
        var speaker = message.Role == ChatHistoryRole.User
            ? "User"
            : turn.IsAmbient
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

    private static IReadOnlyList<SelectedHistoryTurn> SelectTurns(
        IReadOnlyList<ChatHistoryMessage> messages,
        int regularMessageCount,
        int ambientMessageCount)
    {
        var orderedMessages = messages
            .Select((message, sourceIndex) => new { Message = message, SourceIndex = sourceIndex })
            .OrderBy(item => item.Message.CreatedAtUtc)
            .ThenBy(item => item.SourceIndex)
            .ToArray();

        var classifiedTurns = new List<SelectedHistoryTurn>(orderedMessages.Length);
        for (var index = 0; index < orderedMessages.Length; index++)
        {
            var message = orderedMessages[index].Message;
            classifiedTurns.Add(new SelectedHistoryTurn(
                message,
                IsAmbient(message),
                orderedMessages[index].SourceIndex));
        }

        var selected = classifiedTurns
            .Where(turn => !turn.IsAmbient)
            .TakeLast(regularMessageCount)
            .Concat(
                classifiedTurns
                    .Where(turn => turn.IsAmbient)
                    .TakeLast(ambientMessageCount))
            .OrderBy(turn => turn.Message.CreatedAtUtc)
            .ThenBy(turn => turn.SourceIndex)
            .ToArray();

        return selected;
    }

    private static bool IsAmbient(ChatHistoryMessage message)
    {
        return message.Origin == ChatHistoryOrigin.AmbientReply;
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

    private sealed record SelectedHistoryTurn(
        ChatHistoryMessage Message,
        bool IsAmbient,
        int SourceIndex);
}
