namespace DesktopPet.App.Cloud;

public interface IChatService
{
    Task<ChatReply> ReplyAsync(ChatRequest request, CancellationToken cancellationToken);
}
