namespace DesktopPet.App.Overlay;

public sealed record OverlayCommands(
    Action ShowChat,
    Action ShowSettings,
    Action ShowMemories,
    Action StartSpeak);
