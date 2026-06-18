using System.Text.Json.Serialization;

namespace DesktopPet.App.Cloud;

public sealed record OpenRouterSettings(
    [property: JsonIgnore] string? ApiKey,
    string? VisionModelId,
    bool RequireZeroRetention = true);
