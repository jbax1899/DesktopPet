using DesktopPet.App.Settings;
using DesktopPet.App.Observation;

namespace DesktopPet.App.Cloud;

public sealed record ChatRequest(
    string UserMessage,
    ProfileSettings? ProfileSettings = null,
    string? MemoriesContext = null,
    DesktopTurnContext? DesktopContext = null);

public sealed record ChatReply(string Text);
