using System.Text.Json.Serialization;

namespace DesktopPet.App.Cloud;

public sealed record ElevenLabsSettings(
    [property: JsonIgnore] string? ElevenLabsApiKey,
    string? ElevenLabsAgentId,
    string? ElevenLabsVoiceId,
    IReadOnlyList<ElevenLabsPronunciationDictionaryLocator>? PronunciationDictionaries = null,
    IReadOnlyList<CustomPronunciation>? CustomPronunciations = null);

public sealed record ElevenLabsPronunciationDictionaryLocator(
    string? DisplayName,
    string PronunciationDictionaryId,
    string VersionId);

public sealed record CustomPronunciation(
    string Text,
    string Alias);
