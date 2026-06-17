namespace DesktopPet.App.Memory;

public enum ChatHistoryRole
{
    User,
    Bot
}

public sealed record ChatHistoryMessage(
    string Id,
    ChatHistoryRole Role,
    string Text,
    DateTime CreatedAtUtc,
    string? AudioFileName = null);
