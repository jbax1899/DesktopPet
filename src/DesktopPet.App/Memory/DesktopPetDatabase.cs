using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;

namespace DesktopPet.App.Memory;

public sealed class DesktopPetDatabase : IDisposable
{
    private readonly string _dataDirectory;
    private readonly string _databasePath;
    private SqliteConnection? _connection;

    public DesktopPetDatabase()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet"))
    {
    }

    internal DesktopPetDatabase(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
        _databasePath = Path.Combine(_dataDirectory, "memory.db");
    }

    public void Initialize()
    {
        Directory.CreateDirectory(_dataDirectory);
        EnsureConnection();

        using var command = _connection!.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS chat_messages (
                id TEXT PRIMARY KEY,
                role INTEGER NOT NULL,
                text TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                audio_file_name TEXT NULL,
                desktop_context TEXT NULL,
                origin INTEGER NULL,
                context_snapshot_json TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_chat_messages_created_at_utc
                ON chat_messages(created_at_utc);

            CREATE TABLE IF NOT EXISTS memories (
                id TEXT PRIMARY KEY,
                text TEXT NOT NULL,
                created_at_utc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_memories_created_at_utc
                ON memories(created_at_utc);
            """;
        command.ExecuteNonQuery();
    }

    public SqliteConnection OpenConnection()
    {
        EnsureConnection();
        return _connection!;
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
    }

    private void EnsureConnection()
    {
        if (_connection is not null)
        {
            return;
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 5
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        using var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
    }

    internal static string FormatDate(DateTime value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    internal static DateTime ParseDate(string value)
    {
        return DateTime.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }
}
