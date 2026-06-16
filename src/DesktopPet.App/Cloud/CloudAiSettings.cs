namespace DesktopPet.App.Cloud;

public sealed record CloudAiSettings(
    string? ElevenLabsApiKey,
    string? ElevenLabsAgentId,
    string? ElevenLabsVoiceId);
