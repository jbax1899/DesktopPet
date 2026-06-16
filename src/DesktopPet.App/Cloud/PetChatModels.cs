namespace DesktopPet.App.Cloud;

public sealed record PetChatRequest(string UserMessage);

public sealed record PetChatReply(string Text);
