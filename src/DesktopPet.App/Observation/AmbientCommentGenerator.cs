using DesktopPet.App.Cloud;
using DesktopPet.App.Memory;
using DesktopPet.App.Settings;

namespace DesktopPet.App.Observation;

public sealed record AmbientCommentResult(
    string Text,
    string? DesktopContext,
    AgentContextSnapshot? ContextSnapshot);

public interface IAmbientCommentGenerator
{
    Task<AmbientCommentResult?> GenerateAsync(
        DesktopObservationChange change,
        VisionObservation? visionObservation,
        CancellationToken cancellationToken);
}

internal sealed class ElevenLabsAmbientCommentGenerator : IAmbientCommentGenerator
{
    private readonly IChatService _chatService;
    private readonly ObservationStore _observationStore;
    private readonly IChatHistoryStore _chatHistoryStore;
    private readonly IMemoryStore _memoryStore;
    private readonly Func<ProfileSettings> _profileSettingsProvider;
    private readonly IObservationPermissionService _permissionService;

    public ElevenLabsAmbientCommentGenerator(
        IChatService chatService,
        ObservationStore observationStore,
        IChatHistoryStore chatHistoryStore,
        IMemoryStore memoryStore,
        Func<ProfileSettings> profileSettingsProvider,
        IObservationPermissionService permissionService)
    {
        _chatService = chatService;
        _observationStore = observationStore;
        _chatHistoryStore = chatHistoryStore;
        _memoryStore = memoryStore;
        _profileSettingsProvider = profileSettingsProvider;
        _permissionService = permissionService;
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
                _profileSettingsProvider(),
                BuildMemoriesContext(),
                DesktopContext: context,
                ObservationHistory: history,
                ConversationHistory: GetConversationHistory()),
            cancellationToken);

        var text = reply.Text.Trim();
        return string.Equals(text, "SILENCE", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(text)
            ? null
            : new AmbientCommentResult(
                text,
                DesktopContextFormatter.Format(context),
                reply.ContextSnapshot);
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
                .Take(_permissionService.Current.ObservationContextDepth)
                .ToArray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load observation history ({ex.GetType().Name}): {ex.Message}");
            return [];
        }
    }

    private string? BuildMemoriesContext()
    {
        try
        {
            var memories = _memoryStore.List();
            return memories.Count == 0
                ? null
                : string.Join("\n", memories.Select(memory => memory.Text));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load memories for ambient comment ({ex.GetType().Name}): {ex.Message}");
            return null;
        }
    }

    private IReadOnlyList<ChatHistoryMessage> GetConversationHistory()
    {
        try
        {
            return _chatHistoryStore.List();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load conversation history for ambient comment ({ex.GetType().Name}): {ex.Message}");
            return [];
        }
    }

    private string BuildPrompt(ReducedDesktopObservation observation, VisionObservation? visionObservation)
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
            parts.Add($"Possible topics: {string.Join(", ", visionObservation.PossibleCommentTopics.Take(
                _permissionService.Current.CommentTopicLimit))}");
        }

        parts.Add("Give one short, playful, in-character comment as a desktop pet. Be curious and opinionated. Never reply SILENCE.");

        return string.Join(" ", parts);
    }
}
