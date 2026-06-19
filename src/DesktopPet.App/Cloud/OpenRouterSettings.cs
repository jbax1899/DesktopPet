using System.Text.Json.Serialization;

namespace DesktopPet.App.Cloud;

[method: JsonConstructor]
public sealed record OpenRouterSettings(
    [property: JsonIgnore] string? ApiKey,
    string? VisionModelId,
    string? AudioAnalysisModelId,
    bool RequireZeroRetention = true)
{
    public OpenRouterSettings(string? apiKey, string? visionModelId)
        : this(apiKey, visionModelId, null, true)
    {
    }

    public OpenRouterSettings(string? apiKey, string? visionModelId, bool requireZeroRetention)
        : this(apiKey, visionModelId, null, requireZeroRetention)
    {
    }
}
