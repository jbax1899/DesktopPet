using System.IO;
using System.Net.Http;

namespace DesktopPet.App.Cloud;

public sealed record VoiceSynthesisRequest(string Text);

public sealed class VoiceSynthesisResult : IAsyncDisposable
{
    private readonly HttpResponseMessage? _httpResponse;

    public VoiceSynthesisResult(
        Stream audioStream,
        string audioFormat,
        HttpResponseMessage? httpResponse = null)
    {
        AudioStream = audioStream;
        AudioFormat = audioFormat;
        _httpResponse = httpResponse;
    }

    public Stream AudioStream { get; }

    public string AudioFormat { get; }

    public async ValueTask DisposeAsync()
    {
        await AudioStream.DisposeAsync();
        _httpResponse?.Dispose();
    }
}
