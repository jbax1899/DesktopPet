using DesktopPet.App.Cloud;

namespace DesktopPet.App.Observation;

public sealed record AmbientCommentResult(string Text, string? DesktopContext);

public interface IAmbientCommentGenerator
{
    Task<AmbientCommentResult?> GenerateAsync(
        DesktopObservationChange change,
        VisionObservation? visionObservation,
        CancellationToken cancellationToken);
}

internal sealed class ElevenLabsAmbientCommentGenerator : IAmbientCommentGenerator
{
    private const int ObservationHistoryCount = 5;

    private readonly IChatService _chatService;
    private readonly ObservationStore _observationStore;

    public ElevenLabsAmbientCommentGenerator(IChatService chatService, ObservationStore observationStore)
    {
        _chatService = chatService;
        _observationStore = observationStore;
    }

    public async Task<AmbientCommentResult?> GenerateAsync(
        DesktopObservationChange change,
        VisionObservation? visionObservation,
        CancellationToken cancellationToken)
    {
        var observation = change.Observation;
        var context = CreateDesktopContext(change);

        var prompt = BuildPrompt(observation, visionObservation);
        var history = GetRecentObservations();

        var reply = await _chatService.ReplyAsync(
            new ChatRequest(
                prompt,
                DesktopContext: context,
                ObservationHistory: history),
            cancellationToken);

        var text = reply.Text.Trim();
        return string.Equals(text, "SILENCE", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(text)
            ? null
            : new AmbientCommentResult(text, DesktopContextFormatter.Format(context));
    }

    private static DesktopTurnContext CreateDesktopContext(DesktopObservationChange change)
    {
        var observation = change.Observation;
        return new DesktopTurnContext(
            observation.ApplicationName,
            observation.ActivityDescription,
            null,
            observation.Capabilities,
            observation.StructuralDescription);
    }

    private IReadOnlyList<ObservationRecord> GetRecentObservations()
    {
        try
        {
            return _observationStore.List()
                .OrderByDescending(r => r.CapturedAt)
                .Take(ObservationHistoryCount)
                .ToArray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load observation history ({ex.GetType().Name}): {ex.Message}");
            return [];
        }
    }

    private static string BuildPrompt(ReducedDesktopObservation observation, VisionObservation? visionObservation)
    {
        if (visionObservation is null)
        {
            return "Give one short, playful, in-character comment about what the user is doing right now. You are a desktop pet — be curious, observant, and opinionated. Never reply SILENCE.";
        }

        var parts = new List<string>
        {
            $"The user is {observation.ActivityDescription} in {observation.ApplicationName}.",
            $"Summary: {visionObservation.Summary}"
        };

        if (visionObservation.PossibleCommentTopics.Count > 0)
        {
            parts.Add($"Possible topics: {string.Join(", ", visionObservation.PossibleCommentTopics.Take(2))}");
        }

        parts.Add("Give one short, playful, in-character comment as a desktop pet. Be curious and opinionated. Never reply SILENCE.");

        return string.Join(" ", parts);
    }
}
