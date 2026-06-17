using DesktopPet.App.Settings;

namespace DesktopPet.App.Cloud;

public sealed record ChatRequest(string UserMessage, ProfileSettings? ProfileSettings = null);

public sealed record ChatReply(string Text);
