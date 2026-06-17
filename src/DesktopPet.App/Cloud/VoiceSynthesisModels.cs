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
        int sampleRate,
        int bitsPerSample,
        int channels,
        HttpResponseMessage? httpResponse = null)
    {
        AudioStream = audioStream;
        AudioFormat = audioFormat;
        SampleRate = sampleRate;
        BitsPerSample = bitsPerSample;
        Channels = channels;
        _httpResponse = httpResponse;
    }

    public Stream AudioStream { get; }

    public string AudioFormat { get; }

    public int SampleRate { get; }

    public int BitsPerSample { get; }

    public int Channels { get; }

    public async ValueTask DisposeAsync()
    {
        await AudioStream.DisposeAsync();
        _httpResponse?.Dispose();
    }
}
