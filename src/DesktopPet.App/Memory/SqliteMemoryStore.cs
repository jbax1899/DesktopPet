namespace DesktopPet.App.Memory;

public sealed class SqliteMemoryStore : IMemoryStore
{
    private readonly DesktopPetDatabase _database;

    public SqliteMemoryStore(DesktopPetDatabase database)
    {
        _database = database;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<MemoryEntry> List()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, text, created_at_utc
            FROM memories
            ORDER BY created_at_utc, rowid;
            """;

        using var reader = command.ExecuteReader();
        var memories = new List<MemoryEntry>();
        while (reader.Read())
        {
            memories.Add(new MemoryEntry(
                reader.GetString(0),
                reader.GetString(1),
                DesktopPetDatabase.ParseDate(reader.GetString(2))));
        }

        return memories;
    }

    public MemoryEntry Add(string text)
    {
        var trimmedText = text.Trim();
        if (string.IsNullOrWhiteSpace(trimmedText))
        {
            throw new ArgumentException("Memory text cannot be blank.", nameof(text));
        }

        var memory = new MemoryEntry(
            Guid.NewGuid().ToString("N"),
            trimmedText,
            DateTime.UtcNow);

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO memories (id, text, created_at_utc)
            VALUES ($id, $text, $createdAtUtc);
            """;
        command.Parameters.AddWithValue("$id", memory.Id);
        command.Parameters.AddWithValue("$text", memory.Text);
        command.Parameters.AddWithValue("$createdAtUtc", DesktopPetDatabase.FormatDate(memory.CreatedAtUtc));
        command.ExecuteNonQuery();

        Changed?.Invoke(this, EventArgs.Empty);
        return memory;
    }

    public void Delete(string id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM memories WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);

        if (command.ExecuteNonQuery() > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Clear()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM memories;";

        if (command.ExecuteNonQuery() > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
