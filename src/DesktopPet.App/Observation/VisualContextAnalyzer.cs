namespace DesktopPet.App.Observation;

public sealed record VisualAnalysisRequest(string ApplicationName, string? ActivityDescription);

public sealed record VisualContextSummary(
    DesktopContextCollectionStatus Status,
    string? Description);

public interface IVisualContextAnalyzer
{
    bool IsAvailable { get; }

    Task<VisualContextSummary> AnalyzeAsync(
        CapturedWindowImage image,
        VisualAnalysisRequest request,
        CancellationToken cancellationToken);

    Task<VisionObservation?> AnalyzeDetailedAsync(
        CapturedWindowImage image,
        VisualAnalysisRequest request,
        IReadOnlyList<ReducedDesktopObservation> recentObservations,
        DateTimeOffset? lastSpokeAt,
        CancellationToken cancellationToken);
}
