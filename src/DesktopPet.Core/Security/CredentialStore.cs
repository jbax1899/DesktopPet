using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopPet.App.Security;

public sealed class CredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DesktopPet.Credentials.v1");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly object Sync = new();

    private readonly string _filePath;

    public CredentialStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet",
            "credentials.dat"))
    {
    }

    internal CredentialStore(string filePath)
    {
        _filePath = filePath;
    }

    public string? GetElevenLabsApiKey()
    {
        lock (Sync)
        {
            return Load().Credentials.ElevenLabsApiKey;
        }
    }

    public string? GetOpenRouterApiKey()
    {
        lock (Sync)
        {
            return Load().Credentials.OpenRouterApiKey;
        }
    }

    public void SaveElevenLabsApiKey(string? apiKey)
    {
        lock (Sync)
        {
            var loaded = Load();
            EnsureWritable(loaded);
            Save(loaded.Credentials with { ElevenLabsApiKey = Normalize(apiKey) });
        }
    }

    public void SaveOpenRouterApiKey(string? apiKey)
    {
        lock (Sync)
        {
            var loaded = Load();
            EnsureWritable(loaded);
            Save(loaded.Credentials with { OpenRouterApiKey = Normalize(apiKey) });
        }
    }

    private CredentialLoadResult Load()
    {
        if (!File.Exists(_filePath))
        {
            return new CredentialLoadResult(Credentials.Empty, IsUnreadable: false);
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(_filePath);
            var jsonBytes = ProtectedData.Unprotect(
                protectedBytes,
                Entropy,
                DataProtectionScope.CurrentUser);
            var credentials = JsonSerializer.Deserialize<Credentials>(jsonBytes, JsonOptions)
                ?? throw new JsonException("Credential payload is empty.");
            return new CredentialLoadResult(credentials, IsUnreadable: false);
        }
        catch (Exception ex) when (
            ex is CryptographicException
            or IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            return new CredentialLoadResult(Credentials.Empty, IsUnreadable: true);
        }
    }

    private void EnsureWritable(CredentialLoadResult loaded)
    {
        if (loaded.IsUnreadable)
        {
            throw new InvalidDataException(
                $"Credential file '{_filePath}' could not be read. It was not overwritten.");
        }
    }

    private void Save(Credentials credentials)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("Credential path does not have a directory.");
        Directory.CreateDirectory(directory);

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(credentials, JsonOptions);
        var protectedBytes = ProtectedData.Protect(
            jsonBytes,
            Entropy,
            DataProtectionScope.CurrentUser);

        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(protectedBytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record Credentials(
        string? ElevenLabsApiKey,
        string? OpenRouterApiKey)
    {
        public static Credentials Empty { get; } = new(null, null);
    }

    private sealed record CredentialLoadResult(
        Credentials Credentials,
        bool IsUnreadable);
}
