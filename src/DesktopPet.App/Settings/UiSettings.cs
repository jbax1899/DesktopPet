namespace DesktopPet.App.Settings;

public sealed record UiSettings(
    KeyboardShortcut ChatShortcut,
    KeyboardShortcut PushToTalkShortcut,
    OverlayPosition? OverlayPosition = null,
    ChatHistoryContextSettings? ChatHistoryContext = null)
{
    public static UiSettings Default { get; } = new(
        KeyboardShortcut.DefaultChatShortcut,
        KeyboardShortcut.DefaultPushToTalkShortcut,
        ChatHistoryContext: ChatHistoryContextSettings.Default);

    public ChatHistoryContextSettings GetEffectiveChatHistoryContext()
    {
        return (ChatHistoryContext ?? ChatHistoryContextSettings.Default).Normalize();
    }
}
