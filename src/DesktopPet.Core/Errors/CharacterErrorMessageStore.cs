using System.IO;
using System.Text.Json;

namespace DesktopPet.App.Errors;

public sealed class CharacterErrorMessageStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly IReadOnlyDictionary<PetErrorCode, string> DefaultMessages = new Dictionary<PetErrorCode, string>
    {
        [PetErrorCode.MissingApiKey] = "I need my ElevenLabs key before I can talk.",
        [PetErrorCode.MissingAgentId] = "I need my agent ID before I can think out loud.",
        [PetErrorCode.MissingVoiceId] = "I know what to say, but not which voice to use.",
        [PetErrorCode.ChatTimeout] = "I got stuck thinking. Try me again.",
        [PetErrorCode.ChatFailed] = "My brain line dropped. Try me again.",
        [PetErrorCode.TtsFailed] = "I had words, but my voice tripped.",
        [PetErrorCode.PlaybackFailed] = "I made the sound, but could not play it.",
        [PetErrorCode.HotkeyConflict] = "That shortcut is already taken.",
        [PetErrorCode.HotkeyInvalid] = "That shortcut will not work here."
    };

    private static readonly IReadOnlyDictionary<PetErrorCode, string> MessageKeys = new Dictionary<PetErrorCode, string>
    {
        [PetErrorCode.MissingApiKey] = "missing_api_key",
        [PetErrorCode.MissingAgentId] = "missing_agent_id",
        [PetErrorCode.MissingVoiceId] = "missing_voice_id",
        [PetErrorCode.ChatTimeout] = "chat_timeout",
        [PetErrorCode.ChatFailed] = "chat_failed",
        [PetErrorCode.TtsFailed] = "tts_failed",
        [PetErrorCode.PlaybackFailed] = "playback_failed",
        [PetErrorCode.HotkeyConflict] = "hotkey_conflict",
        [PetErrorCode.HotkeyInvalid] = "hotkey_invalid"
    };

    private readonly string _messageFilePath;

    public CharacterErrorMessageStore()
    {
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet");

        _messageFilePath = Path.Combine(settingsDirectory, "character-error-messages.json");
    }

    public string GetMessage(PetErrorCode code)
    {
        var key = MessageKeys[code];
        var customMessages = LoadCustomMessages();
        if (customMessages.TryGetValue(key, out var customMessage)
            && !string.IsNullOrWhiteSpace(customMessage))
        {
            return customMessage.Trim();
        }

        return DefaultMessages[code];
    }

    private Dictionary<string, string> LoadCustomMessages()
    {
        if (!File.Exists(_messageFilePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_messageFilePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? [];
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
}
