namespace DesktopPet.App.Settings;

public sealed record UiSettings(KeyboardShortcut ChatShortcut, OverlayPosition? OverlayPosition = null)
{
    public static UiSettings Default { get; } = new(KeyboardShortcut.DefaultChatShortcut);
}

public sealed record OverlayPosition(double Left, double Top);
