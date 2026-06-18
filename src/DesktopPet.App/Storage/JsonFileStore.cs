using System.IO;
using System.Text;
using System.Text.Json;

namespace DesktopPet.App.Storage;

internal sealed class JsonFileStore<T>
{
    private readonly string _filePath;
    private readonly string _backupPath;
    private readonly Func<string, T> _deserialize;
    private readonly Func<T, string> _serialize;
    private readonly object _sync = new();

    public JsonFileStore(
        string filePath,
        Func<string, T> deserialize,
        Func<T, string> serialize)
    {
        _filePath = filePath;
        _backupPath = $"{filePath}.bak";
        _deserialize = deserialize;
        _serialize = serialize;
    }

    public T Load(T fallback)
    {
        lock (_sync)
        {
            return TryLoad(_filePath, out var value) || TryLoad(_backupPath, out value)
                ? value
                : fallback;
        }
    }

    public void Save(T value)
    {
        var json = _serialize(value);

        lock (_sync)
        {
            var directory = Path.GetDirectoryName(_filePath)
                ?? throw new InvalidOperationException("JSON file path does not have a directory.");
            Directory.CreateDirectory(directory);

            var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                WriteTemporaryFile(temporaryPath, json);

                if (!File.Exists(_filePath))
                {
                    File.Move(temporaryPath, _filePath);
                    return;
                }

                if (IsMalformed(_filePath))
                {
                    File.Move(_filePath, CreateCorruptPath());
                    File.Move(temporaryPath, _filePath);
                    return;
                }

                File.Replace(
                    temporaryPath,
                    _filePath,
                    _backupPath,
                    ignoreMetadataErrors: true);
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }
    }

    public void Delete()
    {
        lock (_sync)
        {
            DeleteIfExists(_backupPath);

            var directory = Path.GetDirectoryName(_filePath);
            if (directory is not null && Directory.Exists(directory))
            {
                var fileName = Path.GetFileName(_filePath);
                foreach (var path in Directory.EnumerateFiles(directory, $"{fileName}.*.corrupt"))
                {
                    DeleteIfExists(path);
                }

                foreach (var path in Directory.EnumerateFiles(directory, $"{fileName}.*.tmp"))
                {
                    DeleteIfExists(path);
                }
            }

            DeleteIfExists(_filePath);
        }
    }

    private bool TryLoad(string path, out T value)
    {
        if (!File.Exists(path))
        {
            value = default!;
            return false;
        }

        try
        {
            value = _deserialize(File.ReadAllText(path));
            return true;
        }
        catch (Exception ex) when (
            ex is JsonException
            or IOException
            or UnauthorizedAccessException)
        {
            value = default!;
            return false;
        }
    }

    private bool IsMalformed(string path)
    {
        try
        {
            _deserialize(File.ReadAllText(path));
            return false;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private string CreateCorruptPath()
    {
        return $"{_filePath}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.corrupt";
    }

    private static void WriteTemporaryFile(string path, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
