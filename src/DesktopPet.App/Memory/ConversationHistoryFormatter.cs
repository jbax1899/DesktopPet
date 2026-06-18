using System.Text;

namespace DesktopPet.App.Memory;

public static class ConversationHistoryFormatter
{
    private const int MaximumTurns = 20;
    private const int MaximumTotalLength = 8_000;
    private const int MaximumTextLength = 600;
    private const int MaximumContextLength = 500;

    public static string? Format(IReadOnlyList<ChatHistoryMessage>? messages)
    {
        if (messages is null || messages.Count == 0)
        {
            return null;
        }

        var newestTurns = messages
            .OrderByDescending(message => message.CreatedAtUtc)
            .Take(MaximumTurns)
            .Select(FormatTurn)
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

    private static string FormatTurn(ChatHistoryMessage message)
    {
        var speaker = message.Role == ChatHistoryRole.User
            ? "User"
            : message.Origin == ChatHistoryOrigin.AmbientReply
                ? "Pet (ambient observation)"
                : "Pet";

        var builder = new StringBuilder();
        builder.Append('[');
        builder.Append(message.CreatedAtUtc.ToString("u"));
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
