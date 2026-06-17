using DesktopPet.App.Settings;

namespace DesktopPet.App.Cloud;

public sealed record PetChatRequest(string UserMessage, PetProfileSettings? ProfileSettings = null);

public sealed record PetChatReply(string Text);
