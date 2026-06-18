using System.Text.Json.Serialization;

namespace DesktopPet.App.Observation;

public sealed record VisionObservation(
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("visible_activity")] string? VisibleActivity,
    [property: JsonPropertyName("notable_changes")] IReadOnlyList<string> NotableChanges,
    [property: JsonPropertyName("possible_comment_topics")] IReadOnlyList<string> PossibleCommentTopics,
    [property: JsonPropertyName("novelty")] double Novelty,
    [property: JsonPropertyName("relevance")] double Relevance,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("sensitivity")] double Sensitivity,
    [property: JsonPropertyName("interruption_cost")] double InterruptionCost,
    [property: JsonPropertyName("expires_after_seconds")] int ExpiresAfterSeconds);

public sealed record ObservationRecord(
    string Id,
    DateTimeOffset CapturedAt,
    string Application,
    string? WindowTitle,
    string Provider,
    string Model,
    VisionObservation Analysis,
    double InterestScore,
    ObservationOutcome Outcome,
    DateTimeOffset? SpokenAt,
    string? ThumbnailPath = null);

public enum ObservationOutcome
{
    Spoken,
    BelowThreshold,
    Cooldown,
    Duplicate,
    UserBusy,
    Stale,
    Sensitive
}
