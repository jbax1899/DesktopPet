namespace DesktopPet.App.Errors;

public enum PetErrorCode
{
    MissingApiKey,
    MissingAgentId,
    MissingVoiceId,
    ChatTimeout,
    ChatFailed,
    TtsFailed,
    PlaybackFailed,
    HotkeyConflict,
    HotkeyInvalid
}
