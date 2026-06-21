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
    bool SupportsAudioInput,
    decimal InputCostPerToken,
    decimal OutputCostPerToken,
    decimal AudioCostPerToken,
    bool IsAudioModel)
{
    public string CostSummary
    {
        get
        {
            if (IsAudioModel)
            {
                if (InputCostPerToken == 0m && OutputCostPerToken == 0m)
                {
                    return "Free";
                }

                if (OutputCostPerToken == 0m)
                {
                    return $"${InputCostPerToken:F6} / audio token";
                }

                return $"${InputCostPerToken:F6} in / ${OutputCostPerToken:F6} out";
            }

            if (InputCostPerToken == 0m && OutputCostPerToken == 0m)
            {
                return "Free";
            }

            if (OutputCostPerToken == 0m)
            {
                return $"${InputCostPerToken * 1_000_000:F2} per M tokens";
            }

            return $"${InputCostPerToken * 1_000_000:F2} / ${OutputCostPerToken * 1_000_000:F2} per M tokens";
        }
    }
}

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

    public async Task<IReadOnlyList<OpenRouterModelInfo>> GetSttModelsAsync(CancellationToken cancellationToken)
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
                "https://openrouter.ai/api/v1/models?output_modalities=transcription");
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
                .Where(m => m.Architecture?.OutputModalities?.Contains("transcription") == true)
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .Select(m => new OpenRouterModelInfo(
                    m.Id,
                    m.Name,
                    m.SupportedParameters?.Contains("response_format") == true,
                    m.Architecture?.InputModalities?.Contains("image") == true,
                    m.Architecture?.InputModalities?.Contains("audio") == true,
                    ParseCost(m.Pricing?.Prompt),
                    ParseCost(m.Pricing?.Completion),
                    ParseCost(m.Pricing?.Audio),
                    IsAudioModel: true))
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

    private static decimal ParseCost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.TryParse(value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var cost)
            ? cost
            : 0m;
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
                    m.Architecture?.InputModalities?.Contains("audio") == true,
                    ParseCost(m.Pricing?.Prompt),
                    ParseCost(m.Pricing?.Completion),
                    0m,
                    IsAudioModel: false))
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
        [property: JsonPropertyName("supported_parameters")] IReadOnlyList<string>? SupportedParameters,
        [property: JsonPropertyName("pricing")] ModelPricing? Pricing);

    private sealed record ModelArchitecture(
        [property: JsonPropertyName("input_modalities")] IReadOnlyList<string>? InputModalities,
        [property: JsonPropertyName("output_modalities")] IReadOnlyList<string>? OutputModalities);

    private sealed record ModelPricing(
        [property: JsonPropertyName("prompt")] string? Prompt,
        [property: JsonPropertyName("completion")] string? Completion,
        [property: JsonPropertyName("audio")] string? Audio);
}
