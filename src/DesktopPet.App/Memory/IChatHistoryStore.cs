namespace DesktopPet.App.Memory;

public interface IChatHistoryStore
{
    IReadOnlyList<ChatHistoryMessage> List();

    ChatHistoryMessage Add(
        ChatHistoryRole role,
        string text,
        ChatHistoryOrigin? origin = null);

    void SetAudioFileName(string id, string audioFileName);

    void SetDesktopContext(string id, string? desktopContext);

    void SetContextSnapshot(string id, AgentContextSnapshot contextSnapshot);
}
