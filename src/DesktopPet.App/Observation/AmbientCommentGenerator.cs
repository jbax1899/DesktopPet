using DesktopPet.App.Cloud;

namespace DesktopPet.App.Observation;

public interface IAmbientCommentGenerator
{
    Task<string?> GenerateAsync(DesktopObservationChange change, CancellationToken cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var observation = change.Observation;
        var context = new DesktopTurnContext(
            observation.ApplicationName,
            observation.ActivityDescription,
            null,
            observation.Capabilities,
            observation.StructuralDescription);

        var reply = await _chatService.ReplyAsync(
            new ChatRequest(
                "Give one brief, useful desktop-pet comment about this permitted change. If there is nothing specific worth saying, reply with SILENCE.",
                DesktopContext: context),
            cancellationToken);

        var text = reply.Text.Trim();
        return string.Equals(text, "SILENCE", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(text)
            ? null
            : text;
    }
}
