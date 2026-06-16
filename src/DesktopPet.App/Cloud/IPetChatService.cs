namespace DesktopPet.App.Cloud;

public interface IPetChatService
{
    Task<PetChatReply> ReplyAsync(PetChatRequest request, CancellationToken cancellationToken);
}
