using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopPet.App.Security;

internal sealed class CredentialStore
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
            return Load().ElevenLabsApiKey;
        }
    }

    public string? GetOpenRouterApiKey()
    {
        lock (Sync)
        {
            return Load().OpenRouterApiKey;
        }
    }

    public void SaveElevenLabsApiKey(string? apiKey)
    {
        lock (Sync)
        {
            var credentials = Load();
            Save(credentials with { ElevenLabsApiKey = Normalize(apiKey) });
        }
    }

    public void SaveOpenRouterApiKey(string? apiKey)
    {
        lock (Sync)
        {
            var credentials = Load();
            Save(credentials with { OpenRouterApiKey = Normalize(apiKey) });
        }
    }

    private Credentials Load()
    {
        if (!File.Exists(_filePath))
        {
            return Credentials.Empty;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(_filePath);
            var jsonBytes = ProtectedData.Unprotect(
                protectedBytes,
                Entropy,
                DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Credentials>(jsonBytes, JsonOptions)
                ?? Credentials.Empty;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or JsonException)
        {
            return Credentials.Empty;
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
}
