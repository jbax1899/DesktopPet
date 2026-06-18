using System.IO;
using System.Text.Json;

namespace DesktopPet.App.Memory;

public sealed class LocalChatHistoryStore : IChatHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _historyFilePath;

    public LocalChatHistoryStore()
    {
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet");

        _historyFilePath = Path.Combine(settingsDirectory, "chat-history.json");
    }

    public IReadOnlyList<ChatHistoryMessage> List()
    {
        return LoadAll()
            .OrderBy(message => message.CreatedAtUtc)
            .ToList();
    }

    public ChatHistoryMessage Add(
        ChatHistoryRole role,
        string text,
        ChatHistoryOrigin? origin = null)
    {
        var trimmedText = text.Trim();
        if (string.IsNullOrWhiteSpace(trimmedText))
        {
            throw new ArgumentException("Chat history text cannot be blank.", nameof(text));
        }

        var message = new ChatHistoryMessage(
            Guid.NewGuid().ToString("N"),
            role,
            trimmedText,
            DateTime.UtcNow,
            Origin: origin);
        var messages = LoadAll();
        messages.Add(message);
        SaveAll(messages);
        return message;
    }

    public void SetAudioFileName(string id, string audioFileName)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(audioFileName))
        {
            return;
        }

        var messages = LoadAll();
        var index = messages.FindIndex(message => message.Id == id);
        if (index < 0)
        {
            return;
        }

        messages[index] = messages[index] with
        {
            AudioFileName = audioFileName
        };
        SaveAll(messages);
    }

    public void SetDesktopContext(string id, string? desktopContext)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var messages = LoadAll();
        var index = messages.FindIndex(message => message.Id == id);
        if (index < 0)
        {
            return;
        }

        messages[index] = messages[index] with
        {
            DesktopContext = desktopContext
        };
        SaveAll(messages);
    }

    private List<ChatHistoryMessage> LoadAll()
    {
        if (!File.Exists(_historyFilePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_historyFilePath);
            return JsonSerializer.Deserialize<List<ChatHistoryMessage>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private void SaveAll(IReadOnlyCollection<ChatHistoryMessage> messages)
    {
        var directory = Path.GetDirectoryName(_historyFilePath)
            ?? throw new InvalidOperationException("Chat history file path does not have a directory.");

        Directory.CreateDirectory(directory);
        File.WriteAllText(_historyFilePath, JsonSerializer.Serialize(messages, JsonOptions));
    }
}
