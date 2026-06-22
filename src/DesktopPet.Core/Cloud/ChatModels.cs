using DesktopPet.App.Settings;
using DesktopPet.App.Observation;
using DesktopPet.App.Memory;

namespace DesktopPet.App.Cloud;

public sealed record ChatRequest(
    string UserMessage,
    ProfileSettings? ProfileSettings = null,
    string? MemoriesContext = null,
    DesktopTurnContext? DesktopContext = null,
    IReadOnlyList<ObservationRecord>? ObservationHistory = null,
    IReadOnlyList<ChatHistoryMessage>? ConversationHistory = null,
    string? AudioObservationHistory = null);

public sealed record ChatReply(string Text, AgentContextSnapshot? ContextSnapshot = null);
