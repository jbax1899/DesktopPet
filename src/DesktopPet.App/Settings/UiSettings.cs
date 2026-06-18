namespace DesktopPet.App.Settings;

public sealed record UiSettings(
    KeyboardShortcut ChatShortcut,
    OverlayPosition? OverlayPosition = null,
    ChatHistoryContextSettings? ChatHistoryContext = null)
{
    public static UiSettings Default { get; } = new(
        KeyboardShortcut.DefaultChatShortcut,
        ChatHistoryContext: ChatHistoryContextSettings.Default);

    public ChatHistoryContextSettings GetEffectiveChatHistoryContext()
    {
        return (ChatHistoryContext ?? ChatHistoryContextSettings.Default).Normalize();
    }
}

public sealed record ChatHistoryContextSettings(
    int RegularMessageCount,
    int AmbientMessageCount)
{
    public const int MinimumMessageCount = 0;
    public const int MaximumMessageCount = 50;
    public const int DefaultRegularMessageCount = 14;
    public const int DefaultAmbientMessageCount = 6;

    public static ChatHistoryContextSettings Default { get; } = new(
        DefaultRegularMessageCount,
        DefaultAmbientMessageCount);

    public ChatHistoryContextSettings Normalize()
    {
        return new ChatHistoryContextSettings(
            Math.Clamp(RegularMessageCount, MinimumMessageCount, MaximumMessageCount),
            Math.Clamp(AmbientMessageCount, MinimumMessageCount, MaximumMessageCount));
    }
}

public sealed record OverlayPosition(double Left, double Top);
