namespace DesktopPet.App.Cloud;

public sealed record ElevenLabsSettings(
    string? ElevenLabsApiKey,
    string? ElevenLabsAgentId,
    string? ElevenLabsVoiceId,
    IReadOnlyList<ElevenLabsPronunciationDictionaryLocator>? PronunciationDictionaries = null);

public sealed record ElevenLabsPronunciationDictionaryLocator(
    string? DisplayName,
    string PronunciationDictionaryId,
    string VersionId);
