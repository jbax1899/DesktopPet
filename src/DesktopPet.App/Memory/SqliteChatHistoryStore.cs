using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace DesktopPet.App.Memory;

public sealed class SqliteChatHistoryStore : IChatHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly DesktopPetDatabase _database;

    public SqliteChatHistoryStore(DesktopPetDatabase database)
    {
        _database = database;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<ChatHistoryMessage> List()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                role,
                text,
                created_at_utc,
                audio_file_name,
                desktop_context,
                origin,
                context_snapshot_json
            FROM chat_messages
            ORDER BY created_at_utc, rowid;
            """;

        using var reader = command.ExecuteReader();
        var messages = new List<ChatHistoryMessage>();
        while (reader.Read())
        {
            messages.Add(ReadMessage(reader));
        }

        return messages;
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

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO chat_messages (
                id,
                role,
                text,
                created_at_utc,
                origin)
            VALUES (
                $id,
                $role,
                $text,
                $createdAtUtc,
                $origin);
            """;
        command.Parameters.AddWithValue("$id", message.Id);
        command.Parameters.AddWithValue("$role", (int)message.Role);
        command.Parameters.AddWithValue("$text", message.Text);
        command.Parameters.AddWithValue("$createdAtUtc", DesktopPetDatabase.FormatDate(message.CreatedAtUtc));
        command.Parameters.AddWithValue(
            "$origin",
            message.Origin is null ? DBNull.Value : (int)message.Origin.Value);
        command.ExecuteNonQuery();

        Changed?.Invoke(this, EventArgs.Empty);
        return message;
    }

    public void SetAudioFileName(string id, string audioFileName)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(audioFileName))
        {
            return;
        }

        UpdateValue(
            id,
            "audio_file_name",
            audioFileName);
    }

    public void SetDesktopContext(string id, string? desktopContext)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        UpdateValue(
            id,
            "desktop_context",
            desktopContext);
    }

    public void SetContextSnapshot(string id, AgentContextSnapshot contextSnapshot)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        UpdateValue(
            id,
            "context_snapshot_json",
            JsonSerializer.Serialize(contextSnapshot, JsonOptions));
    }

    private void UpdateValue(string id, string columnName, string? value)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE chat_messages SET {columnName} = $value WHERE id = $id;";
        command.Parameters.AddWithValue("$value", value is null ? DBNull.Value : value);
        command.Parameters.AddWithValue("$id", id);

        if (command.ExecuteNonQuery() > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static ChatHistoryMessage ReadMessage(SqliteDataReader reader)
    {
        var contextSnapshotJson = reader.IsDBNull(7) ? null : reader.GetString(7);
        var contextSnapshot = string.IsNullOrWhiteSpace(contextSnapshotJson)
            ? null
            : JsonSerializer.Deserialize<AgentContextSnapshot>(contextSnapshotJson, JsonOptions);

        return new ChatHistoryMessage(
            reader.GetString(0),
            (ChatHistoryRole)reader.GetInt32(1),
            reader.GetString(2),
            DesktopPetDatabase.ParseDate(reader.GetString(3)),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : (ChatHistoryOrigin)reader.GetInt32(6),
            contextSnapshot);
    }
}
