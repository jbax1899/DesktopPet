namespace DesktopPet.App.Audio;

public sealed class TranscriptWorkingBuffer
{
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
    private readonly Func<TimeSpan> _retentionProvider;
    private readonly List<TranscriptWorkingChunk> _chunks = [];

    public TranscriptWorkingBuffer(
        Func<TimeSpan>? retentionProvider = null,
        TimeProvider? timeProvider = null)
    {
        _retentionProvider = retentionProvider ?? (() => TimeSpan.FromSeconds(300));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IReadOnlyList<TranscriptWorkingChunk> List()
    {
        lock (_sync)
        {
            Prune();
            return _chunks.OrderBy(item => item.StartedAt).ToArray();
        }
    }

    public TranscriptWorkingChunk Add(
        CompletedAudioSegment segment,
        string text,
        double confidence)
    {
        lock (_sync)
        {
            Prune();
            var chunk = new TranscriptWorkingChunk(
                Guid.NewGuid().ToString("N"),
                segment.Id,
                segment.Source,
                segment.StartedAt,
                segment.EndedAt,
                text,
                confidence,
                _timeProvider.GetUtcNow() + _retentionProvider());
            _chunks.Add(chunk);
            return chunk;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _chunks.Clear();
        }
    }

    private void Prune()
    {
        var now = _timeProvider.GetUtcNow();
        _chunks.RemoveAll(item => item.ExpiresAt <= now);
    }
}
