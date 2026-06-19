using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopPet.App.Cloud;

public sealed record OpenRouterModelInfo(
    string Id,
    string Name,
    bool SupportsStructuredOutput,
    bool SupportsImageInput,
    bool SupportsAudioInput);

public sealed class OpenRouterModelsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _httpClient;
    private readonly Func<OpenRouterSettings> _settingsProvider;

    public OpenRouterModelsService(HttpClient httpClient, Func<OpenRouterSettings> settingsProvider)
    {
        _httpClient = httpClient;
        _settingsProvider = settingsProvider;
    }

    public async Task<IReadOnlyList<OpenRouterModelInfo>> GetVisionModelsAsync(CancellationToken cancellationToken)
    {
        return await GetModelsAsync("image", requireStructuredOutput: false, cancellationToken);
    }

    public async Task<IReadOnlyList<OpenRouterModelInfo>> GetAudioModelsAsync(CancellationToken cancellationToken)
    {
        return await GetModelsAsync("audio", requireStructuredOutput: true, cancellationToken);
    }

    private async Task<IReadOnlyList<OpenRouterModelInfo>> GetModelsAsync(
        string inputModality,
        bool requireStructuredOutput,
        CancellationToken cancellationToken)
    {
        var settings = _settingsProvider();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return [];
        }

        using var timeout = new CancellationTokenSource(RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://openrouter.ai/api/v1/models?input_modalities={inputModality}");
            request.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");

            using var response = await _httpClient.SendAsync(request, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var modelsResponse = await response.Content.ReadFromJsonAsync<ModelsResponse>(JsonOptions, linked.Token);
            if (modelsResponse?.Data is null)
            {
                return [];
            }

            return modelsResponse.Data
                .Where(m => m.Architecture?.InputModalities?.Contains(inputModality) == true)
                .Where(m => !requireStructuredOutput
                    || m.SupportedParameters?.Contains("response_format") == true)
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .Select(m => new OpenRouterModelInfo(
                    m.Id,
                    m.Name,
                    m.SupportedParameters?.Contains("response_format") == true,
                    m.Architecture?.InputModalities?.Contains("image") == true,
                    m.Architecture?.InputModalities?.Contains("audio") == true))
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    private sealed record ModelsResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<ModelEntry>? Data);

    private sealed record ModelEntry(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("architecture")] ModelArchitecture? Architecture,
        [property: JsonPropertyName("supported_parameters")] IReadOnlyList<string>? SupportedParameters);

    private sealed record ModelArchitecture(
        [property: JsonPropertyName("input_modalities")] IReadOnlyList<string>? InputModalities);
}
