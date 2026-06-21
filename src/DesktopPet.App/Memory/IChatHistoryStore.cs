namespace DesktopPet.App.Memory;

public interface IChatHistoryStore
{
    event EventHandler? Changed;

    IReadOnlyList<ChatHistoryMessage> List();

    ChatHistoryMessage Add(
        ChatHistoryRole role,
        string text,
        ChatHistoryOrigin? origin = null);

    void SetAudioFileName(string id, string audioFileName);

    void SetDesktopContext(string id, string? desktopContext);

    void SetContextSnapshot(string id, AgentContextSnapshot contextSnapshot);

    void Delete(string id);

    void Clear();
}
