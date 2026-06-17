namespace DesktopPet.App.Settings;

public sealed record UiSettings(KeyboardShortcut ChatShortcut)
{
    public static UiSettings Default { get; } = new(KeyboardShortcut.DefaultChatShortcut);
}
