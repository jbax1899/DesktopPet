using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DesktopPet.App.Errors;

namespace DesktopPet.App.Cloud;

public sealed class ElevenLabsVoiceSynthesisService : IVoiceSynthesisService
{
    private const string OutputFormat = "mp3_44100_128";
    private const string AudioFormat = "mp3";
    private const string ModelId = "eleven_v3";

    private readonly HttpClient _httpClient;
    private readonly Func<ElevenLabsSettings> _settingsProvider;

    public ElevenLabsVoiceSynthesisService(HttpClient httpClient, Func<ElevenLabsSettings> settingsProvider)
    {
        _httpClient = httpClient;
        _settingsProvider = settingsProvider;
    }

    public async Task<VoiceSynthesisResult> SynthesizeAsync(VoiceSynthesisRequest request, CancellationToken cancellationToken)
    {
        var settings = _settingsProvider();
        if (string.IsNullOrWhiteSpace(settings.ElevenLabsApiKey))
        {
            throw new PetErrorException(PetErrorCode.MissingApiKey, "ElevenLabs API key is missing. Add it in Settings first.");
        }

        if (string.IsNullOrWhiteSpace(settings.ElevenLabsVoiceId))
        {
            throw new PetErrorException(PetErrorCode.MissingVoiceId, "ElevenLabs voice ID is missing. Add it in Settings first.");
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new PetErrorException(PetErrorCode.TtsFailed, "There is no reply to speak yet.");
        }

        var escapedVoiceId = Uri.EscapeDataString(settings.ElevenLabsVoiceId);
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.elevenlabs.io/v1/text-to-speech/{escapedVoiceId}/stream?output_format={OutputFormat}")
        {
            Content = JsonContent.Create(CreateRequestBody(request.Text, settings))
        };

        // ElevenLabs uses this header instead of Authorization.
        httpRequest.Headers.Add("xi-api-key", settings.ElevenLabsApiKey);

        var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new PetErrorException(PetErrorCode.TtsFailed, $"ElevenLabs request failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        try
        {
            var audioStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return new VoiceSynthesisResult(audioStream, AudioFormat, response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static ElevenLabsTextToSpeechRequest CreateRequestBody(string text, ElevenLabsSettings settings)
    {
        var locators = settings.PronunciationDictionaries?
            .Where(locator =>
                !string.IsNullOrWhiteSpace(locator.PronunciationDictionaryId)
                && !string.IsNullOrWhiteSpace(locator.VersionId))
            .Take(3)
            .Select(locator => new ElevenLabsPronunciationDictionaryRequestLocator(
                locator.PronunciationDictionaryId.Trim(),
                locator.VersionId.Trim()))
            .ToArray();

        return new ElevenLabsTextToSpeechRequest(
            text,
            ModelId,
            locators is { Length: > 0 } ? locators : null);
    }

    private sealed record ElevenLabsTextToSpeechRequest(
        string text,
        string model_id,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ElevenLabsPronunciationDictionaryRequestLocator>? pronunciation_dictionary_locators);

    private sealed record ElevenLabsPronunciationDictionaryRequestLocator(
        string pronunciation_dictionary_id,
        string version_id);
}
