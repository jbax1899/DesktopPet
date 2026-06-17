namespace DesktopPet.App.Memory;

public interface IChatHistoryStore
{
    IReadOnlyList<ChatHistoryMessage> List();

    ChatHistoryMessage Add(ChatHistoryRole role, string text);

    void SetAudioFileName(string id, string audioFileName);
}
