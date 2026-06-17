namespace DesktopPet.App.Cloud;

public sealed record ElevenLabsSettings(
    string? ElevenLabsApiKey,
    string? ElevenLabsAgentId,
    string? ElevenLabsVoiceId);
