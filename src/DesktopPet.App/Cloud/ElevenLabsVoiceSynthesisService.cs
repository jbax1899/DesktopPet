using System.Net.Http;
using System.Net.Http.Json;
using DesktopPet.App.Errors;

namespace DesktopPet.App.Cloud;

public sealed class ElevenLabsVoiceSynthesisService : IVoiceSynthesisService
{
    private const string OutputFormat = "mp3_44100_128";
    private const string ModelId = "eleven_multilingual_v2";

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
            $"https://api.elevenlabs.io/v1/text-to-speech/{escapedVoiceId}?output_format={OutputFormat}")
        {
            Content = JsonContent.Create(new
            {
                text = request.Text,
                model_id = ModelId
            })
        };

        // ElevenLabs uses this header instead of Authorization.
        httpRequest.Headers.Add("xi-api-key", settings.ElevenLabsApiKey);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new PetErrorException(PetErrorCode.TtsFailed, $"ElevenLabs request failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new VoiceSynthesisResult(bytes, "mp3");
    }
}
