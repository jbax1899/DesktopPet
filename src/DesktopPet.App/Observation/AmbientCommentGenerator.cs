using DesktopPet.App.Cloud;

namespace DesktopPet.App.Observation;

public interface IAmbientCommentGenerator
{
    Task<string?> GenerateAsync(DesktopObservationChange change, VisionObservation? visionObservation, CancellationToken cancellationToken);
}

internal sealed class ElevenLabsAmbientCommentGenerator : IAmbientCommentGenerator
{
    private readonly IChatService _chatService;

    public ElevenLabsAmbientCommentGenerator(IChatService chatService)
    {
        _chatService = chatService;
    }

    public async Task<string?> GenerateAsync(
        DesktopObservationChange change,
        VisionObservation? visionObservation,
        CancellationToken cancellationToken)
    {
        var observation = change.Observation;
        var context = new DesktopTurnContext(
            observation.ApplicationName,
            observation.ActivityDescription,
            null,
            observation.Capabilities,
            observation.StructuralDescription);

        var prompt = BuildPrompt(observation, visionObservation);

        var reply = await _chatService.ReplyAsync(
            new ChatRequest(
                prompt,
                DesktopContext: context),
            cancellationToken);

        var text = reply.Text.Trim();
        return string.Equals(text, "SILENCE", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(text)
            ? null
            : text;
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
