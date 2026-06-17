using System.IO;

namespace DesktopPet.App.Memory;

public sealed class ChatAudioStore
{
    public const string AudioFormat = "mp3";
    private const int FileBufferSize = 64 * 1024;

    private readonly string _audioDirectory;

    public ChatAudioStore()
    {
        _audioDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet",
            "chat-audio");
    }

    public string CreateAudioFileName(string messageId)
    {
        return $"{messageId}.mp3";
    }

    public FileStream CreateAudioFile(string audioFileName)
    {
        Directory.CreateDirectory(_audioDirectory);
        return new FileStream(
            ResolvePath(audioFileName),
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            FileBufferSize);
    }

    public FileStream OpenRead(string audioFileName)
    {
        return new FileStream(
            ResolvePath(audioFileName),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileBufferSize);
    }

    public bool Exists(string? audioFileName)
    {
        return !string.IsNullOrWhiteSpace(audioFileName)
            && HasMp3Extension(audioFileName)
            && File.Exists(ResolvePath(audioFileName));
    }

    public void Delete(string? audioFileName)
    {
        if (string.IsNullOrWhiteSpace(audioFileName) || !HasMp3Extension(audioFileName))
        {
            return;
        }

        try
        {
            var path = ResolvePath(audioFileName);
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

    private static bool HasMp3Extension(string audioFileName)
    {
        return string.Equals(Path.GetExtension(audioFileName), ".mp3", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolvePath(string audioFileName)
    {
        var safeFileName = Path.GetFileName(audioFileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            throw new ArgumentException("Audio file name cannot be blank.", nameof(audioFileName));
        }

        return Path.Combine(_audioDirectory, safeFileName);
    }
}
