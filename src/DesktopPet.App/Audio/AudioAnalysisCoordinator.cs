using System.Text;

namespace DesktopPet.App.Audio;

public sealed class AudioAnalysisCoordinator : IDisposable
{
    internal const int MaximumQueuedSegments = 2;

    private readonly object _sync = new();
    private readonly IAudioSegmentAnalyzer _analyzer;
    private readonly TranscriptWorkingBuffer _transcriptBuffer;
    private readonly AudioObservationStore _observationStore;
    private readonly TimeProvider _timeProvider;
    private readonly Queue<CompletedAudioSegment> _queue = new();

    private CancellationTokenSource _activeCancellation = new();
    private AudioContextSettings _settings = AudioContextSettings.Default;
    private bool _requestActive;
    private bool _disposed;
    private long _workerVersion;
    private int _successfulCount;
    private int _failureCount;
    private int _droppedCount;
    private string? _lastSafeFailure;
    private DateTimeOffset? _lastSuccessAt;

    public AudioAnalysisCoordinator(
        IAudioSegmentAnalyzer analyzer,
        TranscriptWorkingBuffer transcriptBuffer,
        AudioObservationStore observationStore,
        TimeProvider? timeProvider = null)
    {
        _analyzer = analyzer;
        _transcriptBuffer = transcriptBuffer;
        _observationStore = observationStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AudioAnalysisDiagnostic Diagnostic
    {
        get
        {
            lock (_sync)
            {
                return new AudioAnalysisDiagnostic(
                    _settings.AnalysisEnabled,
                    _analyzer.IsAvailable,
                    _requestActive,
                    _queue.Count,
                    _successfulCount,
                    _failureCount,
                    _droppedCount,
                    _lastSafeFailure,
                    _lastSuccessAt);
            }
        }
    }

    public void ApplySettings(AudioContextSettings settings)
    {
        List<CompletedAudioSegment>? dropped = null;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _settings = settings.Normalize();
            if (!_settings.AnalysisEnabled)
            {
                _activeCancellation.Cancel();
                dropped = DrainQueue();
                _requestActive = false;
                _workerVersion++;
            }
        }

        if (!settings.AnalysisEnabled)
        {
            _transcriptBuffer.Clear();
            DisposeSegments(dropped);
        }

        _observationStore.ApplyRetentionLimit();
    }

    public bool TryEnqueue(CompletedAudioSegment segment)
    {
        lock (_sync)
        {
            if (_disposed || !_settings.AnalysisEnabled || !_analyzer.IsAvailable)
            {
                _droppedCount++;
                return false;
            }

            if (_queue.Count >= MaximumQueuedSegments)
            {
                _droppedCount++;
                return false;
            }

            if (!_requestActive)
            {
                _requestActive = true;
                _activeCancellation.Dispose();
                _activeCancellation = new CancellationTokenSource();
                var workerVersion = ++_workerVersion;
                _ = Task.Run(() => ProcessQueueAsync(
                    segment,
                    _activeCancellation.Token,
                    workerVersion));
            }
            else
            {
                _queue.Enqueue(segment);
            }

            return true;
        }
    }

    public void ClearObservations()
    {
        List<CompletedAudioSegment> dropped;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activeCancellation.Cancel();
            dropped = DrainQueue();
            _requestActive = false;
            _workerVersion++;
        }

        DisposeSegments(dropped);
        _transcriptBuffer.Clear();
        _observationStore.Clear();
    }

    public void DeleteObservation(AudioObservation observation)
    {
        _transcriptBuffer.DeleteSegment(observation.SegmentId);
        _observationStore.Delete(observation.Id);
    }

    public void Dispose()
    {
        List<CompletedAudioSegment> dropped;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _activeCancellation.Cancel();
            dropped = DrainQueue();
            _requestActive = false;
            _workerVersion++;
        }

        DisposeSegments(dropped);
        _transcriptBuffer.Clear();
        _activeCancellation.Dispose();
    }

    private async Task ProcessQueueAsync(
        CompletedAudioSegment currentSegment,
        CancellationToken cancellationToken,
        long workerVersion)
    {
        while (true)
        {
            AudioContextSettings settings;
            lock (_sync)
            {
                if (_disposed || cancellationToken.IsCancellationRequested)
                {
                    SetInactive(workerVersion);
                    currentSegment.Dispose();
                    return;
                }

                settings = _settings;
            }

            try
            {
                var response = await _analyzer.AnalyzeAsync(
                    currentSegment,
                    CreateAnalysisOptions(settings),
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                ProcessResponse(currentSegment, response, settings);
            }
            catch (OperationCanceledException)
            {
                RecordFailure("Audio analysis cancelled.");
            }
            catch
            {
                RecordFailure("Audio analysis failed.");
            }
            finally
            {
                currentSegment.Dispose();
            }

            lock (_sync)
            {
                if (workerVersion != _workerVersion)
                {
                    return;
                }

                if (_disposed || cancellationToken.IsCancellationRequested || _queue.Count == 0)
                {
                    SetInactive(workerVersion);
                    return;
                }

                currentSegment = _queue.Dequeue();
            }
        }
    }

    private void SetInactive(long workerVersion)
    {
        if (workerVersion == _workerVersion)
        {
            _requestActive = false;
        }
    }

    private void ProcessResponse(
        CompletedAudioSegment segment,
        AudioAnalysisResponse response,
        AudioContextSettings settings)
    {
        var analysis = response.Analysis;
        if (analysis is null
            || response.Status is not (AudioAnalysisStatus.Success or AudioAnalysisStatus.Partial))
        {
            RecordFailure(response.Failure?.SafeMessage ?? SafeStatus(response.Status));
            return;
        }

        TranscriptWorkingChunk? transcriptChunk = null;
        if (analysis.Confidence >= settings.MinimumAnalysisConfidence
            && !string.IsNullOrWhiteSpace(analysis.Transcript))
        {
            transcriptChunk = _transcriptBuffer.Add(
                segment,
                analysis.Transcript.Trim(),
                analysis.Confidence);
        }

        if (analysis.ShouldStore
            && analysis.Confidence >= settings.MinimumAnalysisConfidence
            && analysis.DetectedKind != AudioDetectedKind.Silence
            && !string.IsNullOrWhiteSpace(analysis.Summary))
        {
            var persistExcerpt = segment.Source == AudioSourceKind.Microphone
                ? settings.PersistMicrophoneTranscriptExcerpt
                : settings.PersistSystemAudioTranscriptExcerpt;
            var excerpt = persistExcerpt
                ? CreateExcerpt(analysis.Transcript)
                : null;

            _observationStore.Add(new AudioObservation(
                Guid.NewGuid().ToString("N"),
                segment.Id,
                segment.Source,
                analysis.DetectedKind,
                segment.StartedAt,
                segment.EndedAt,
                analysis.Summary.Trim(),
                analysis.EventLabels,
                excerpt,
                analysis.Confidence,
                analysis.Sensitivity,
                response.Provider,
                response.Model,
                response.Status,
                _timeProvider.GetUtcNow(),
                transcriptChunk?.ExpiresAt));
        }

        lock (_sync)
        {
            _successfulCount++;
            _lastSuccessAt = _timeProvider.GetUtcNow();
            _lastSafeFailure = null;
        }
    }

    private void RecordFailure(string message)
    {
        lock (_sync)
        {
            _failureCount++;
            _lastSafeFailure = message;
        }
    }

    private static AudioAnalysisOptions CreateAnalysisOptions(AudioContextSettings settings)
    {
        return settings.TranscriptDetail switch
        {
            AudioTranscriptDetail.Brief => new AudioAnalysisOptions(
                RequestTranscript: false,
                MaximumSummaryCharacters: 160,
                MaximumEventLabels: 3,
                TranscriptDetail: AudioTranscriptDetail.Brief,
                MaximumTranscriptCharacters: 0),
            AudioTranscriptDetail.Transcript => new AudioAnalysisOptions(
                RequestTranscript: true,
                MaximumSummaryCharacters: 400,
                MaximumEventLabels: 5,
                TranscriptDetail: AudioTranscriptDetail.Transcript,
                MaximumTranscriptCharacters: 4000),
            _ => new AudioAnalysisOptions(
                RequestTranscript: true,
                MaximumSummaryCharacters: 260,
                MaximumEventLabels: 5,
                TranscriptDetail: AudioTranscriptDetail.Detailed,
                MaximumTranscriptCharacters: 1200)
        };
    }

    private List<CompletedAudioSegment> DrainQueue()
    {
        var result = new List<CompletedAudioSegment>(_queue.Count);
        while (_queue.TryDequeue(out var segment))
        {
            result.Add(segment);
            _droppedCount++;
        }

        return result;
    }

    private static void DisposeSegments(IEnumerable<CompletedAudioSegment>? segments)
    {
        if (segments is null)
        {
            return;
        }

        foreach (var segment in segments)
        {
            segment.Dispose();
        }
    }

    private static string SafeStatus(AudioAnalysisStatus status) => status switch
    {
        AudioAnalysisStatus.Unavailable => "Audio analyzer unavailable.",
        AudioAnalysisStatus.TimedOut => "Audio analysis timed out.",
        AudioAnalysisStatus.Cancelled => "Audio analysis cancelled.",
        AudioAnalysisStatus.ProviderRejected => "Audio provider rejected the request.",
        AudioAnalysisStatus.InvalidResponse => "Audio provider returned an invalid response.",
        _ => "Audio analysis failed."
    };

    internal static string? CreateExcerpt(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return null;
        }

        var builder = new StringBuilder(transcript.Length);
        foreach (var character in transcript)
        {
            if (char.IsControl(character))
            {
                if (char.IsWhiteSpace(character))
                {
                    builder.Append(' ');
                }

                continue;
            }

            builder.Append(character);
        }

        var oneLine = string.Join(
            " ",
            builder.ToString().Split(
                [' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length <= 160 ? oneLine : oneLine[..160];
    }
}
