namespace DesktopPet.App.Cloud;

public sealed record OpenRouterSettings(
    string? ApiKey,
    string? VisionModelId,
    bool RequireZeroRetention = true);
