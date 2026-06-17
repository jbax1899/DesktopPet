namespace DesktopPet.App.Overlay;

public sealed record PetOverlayCommands(
    Action ShowChat,
    Action ShowSettings,
    Action StartSpeak);
