namespace DesktopPet.App.Memory;

public enum ChatHistoryRole
{
    User,
    Bot
}

public enum ChatHistoryOrigin
{
    User,
    DirectReply,
    AmbientReply
}

public sealed record ChatHistoryMessage(
    string Id,
    ChatHistoryRole Role,
    string Text,
    DateTime CreatedAtUtc,
    string? AudioFileName = null,
    string? DesktopContext = null,
    ChatHistoryOrigin? Origin = null);
