using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopPet.App.Cloud;

public sealed class ElevenLabsPronunciationService
{
    public const string ManagedDictionaryName = "DesktopPet custom pronunciations";

    private readonly HttpClient _httpClient;

    public ElevenLabsPronunciationService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<ElevenLabsPronunciationDictionaryLocator>> SyncAsync(
        string apiKey,
        IReadOnlyList<CustomPronunciation> pronunciations,
        IReadOnlyList<ElevenLabsPronunciationDictionaryLocator>? currentLocators,
        CancellationToken cancellationToken)
    {
        var locators = currentLocators?.ToList() ?? [];
        var managedLocator = locators.FirstOrDefault(locator =>
            string.Equals(locator.DisplayName, ManagedDictionaryName, StringComparison.Ordinal));

        if (pronunciations.Count == 0)
        {
            return locators;
        }

        var rules = pronunciations
            .Select(pronunciation => new AliasRule(
                pronunciation.Text,
                "alias",
                pronunciation.Alias))
            .ToArray();

        DictionaryVersionResponse response;
        if (managedLocator is null)
        {
            response = await SendAsync<DictionaryVersionResponse>(
                HttpMethod.Post,
                "https://api.elevenlabs.io/v1/pronunciation-dictionaries/add-from-rules",
                apiKey,
                new CreateDictionaryRequest(
                    rules,
                    ManagedDictionaryName,
                    "Pronunciations managed by DesktopPet."),
                cancellationToken);
        }
        else
        {
            var dictionaryId = Uri.EscapeDataString(managedLocator.PronunciationDictionaryId);
            response = await SendAsync<DictionaryVersionResponse>(
                HttpMethod.Post,
                $"https://api.elevenlabs.io/v1/pronunciation-dictionaries/{dictionaryId}/set-rules",
                apiKey,
                new SetRulesRequest(rules),
                cancellationToken);
            locators.Remove(managedLocator);
        }

        locators.Insert(0, new ElevenLabsPronunciationDictionaryLocator(
            ManagedDictionaryName,
            response.Id,
            response.VersionId));
        return locators;
    }

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string url,
        string apiKey,
        object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("xi-api-key", apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"ElevenLabs could not save pronunciations ({(int)response.StatusCode} {response.ReasonPhrase}).");
        }

        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken)
            ?? throw new JsonException("ElevenLabs returned an empty pronunciation response.");
    }

    private sealed record AliasRule(
        [property: JsonPropertyName("string_to_replace")] string StringToReplace,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("alias")] string Alias);

    private sealed record CreateDictionaryRequest(
        [property: JsonPropertyName("rules")] IReadOnlyList<AliasRule> Rules,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description);

    private sealed record SetRulesRequest(
        [property: JsonPropertyName("rules")] IReadOnlyList<AliasRule> Rules);

    private sealed record DictionaryVersionResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("version_id")] string VersionId);
}
