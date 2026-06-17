using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using NAudio.Wave;

namespace DesktopPet.App.Voice;

public sealed class StreamingMp3AudioPlayer : IDisposable
{
    private static readonly TimeSpan PlaybackStartBufferDuration = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan AmplitudeFrameDuration = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan BufferDuration = TimeSpan.FromSeconds(5);
    private const double NoiseGate = 0.08;
    private const double Attack = 0.75;
    private const double Release = 0.35;

    private readonly object _playbackLock = new();
    private WaveOutEvent? _waveOut;

    public Task PlayAsync(
        Stream audioStream,
        string audioFormat,
        CancellationToken cancellationToken,
        Action<double>? mouthOpenChanged = null,
        Stream? cacheStream = null)
    {
        if (!string.Equals(audioFormat, "mp3", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported audio format: {audioFormat}");
        }

        return Task.Run(
            () => PlayCore(audioStream, cancellationToken, mouthOpenChanged, cacheStream),
            cancellationToken);
    }

    public void Dispose()
    {
        StopAndClearCurrentPlayback();
    }

    private void PlayCore(
        Stream audioStream,
        CancellationToken cancellationToken,
        Action<double>? mouthOpenChanged,
        Stream? cacheStream)
    {
        StopAndClearCurrentPlayback();

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            StopAndClearCurrentPlayback();
            TryDispose(audioStream);
        });

        var firstFrame = LoadNextFrame(audioStream, cancellationToken);
        if (firstFrame is null)
        {
            return;
        }

        using var decompressor = new AcmMp3FrameDecompressor(CreateMp3WaveFormat(firstFrame));
        var waveFormat = decompressor.OutputFormat;
        var bufferedProvider = new BufferedWaveProvider(waveFormat)
        {
            BufferDuration = BufferDuration,
            DiscardOnBufferOverflow = false
        };
        using var waveOut = new WaveOutEvent();
        var playbackState = new PlaybackStopState();
        var playbackComplete = false;
        var amplitudeFrames = new ConcurrentQueue<double>();
        var amplitudeAnalyzer = mouthOpenChanged is null
            ? null
            : new DecodedAmplitudeAnalyzer(waveFormat.SampleRate, waveFormat.BitsPerSample, waveFormat.Channels);
        var mouthTask = mouthOpenChanged is null
            ? null
            : Task.Run(() => ReportAmplitudeUntilComplete(amplitudeFrames, mouthOpenChanged, () => playbackComplete, cancellationToken));
        var playbackStarted = false;
        var decodeBuffer = new byte[Math.Max(waveFormat.AverageBytesPerSecond, waveFormat.BlockAlign * 4096)];

        waveOut.PlaybackStopped += OnPlaybackStopped;
        waveOut.Init(bufferedProvider);
        SetCurrentPlayback(waveOut);

        try
        {
            var startThresholdBytes = BytesForDuration(waveFormat, PlaybackStartBufferDuration);
            var frame = firstFrame;

            while (frame is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfPlaybackFailed(playbackState);
                cacheStream?.Write(frame.RawData, 0, frame.RawData.Length);

                var bytesDecoded = decompressor.DecompressFrame(frame, decodeBuffer, 0);
                if (bytesDecoded > 0)
                {
                    WaitForBufferSpace(bufferedProvider, bytesDecoded, playbackState, cancellationToken);
                    bufferedProvider.AddSamples(decodeBuffer, 0, bytesDecoded);
                    amplitudeAnalyzer?.AddDecodedSamples(decodeBuffer.AsSpan(0, bytesDecoded), amplitudeFrames);
                }

                if (!playbackStarted && bufferedProvider.BufferedBytes >= startThresholdBytes)
                {
                    playbackStarted = true;
                    waveOut.Play();
                }

                frame = LoadNextFrame(audioStream, cancellationToken);
            }

            cacheStream?.Flush();
            amplitudeAnalyzer?.Flush(amplitudeFrames);

            if (!playbackStarted && bufferedProvider.BufferedBytes > 0)
            {
                playbackStarted = true;
                waveOut.Play();
            }

            if (playbackStarted)
            {
                WaitForBufferedPlaybackToDrain(bufferedProvider, waveOut, playbackState, cancellationToken);
            }
        }
        finally
        {
            playbackComplete = true;
            WaitForMouthTask(mouthTask);
            waveOut.PlaybackStopped -= OnPlaybackStopped;
            mouthOpenChanged?.Invoke(0);
            StopAndClearCurrentPlayback();
        }

        return;

        void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            lock (playbackState)
            {
                playbackState.Exception = e.Exception;
                playbackState.IsStopped = true;
                Monitor.PulseAll(playbackState);
            }
        }
    }

    private void SetCurrentPlayback(WaveOutEvent waveOut)
    {
        lock (_playbackLock)
        {
            _waveOut = waveOut;
        }
    }

    private void StopAndClearCurrentPlayback()
    {
        lock (_playbackLock)
        {
            _waveOut?.Stop();
            _waveOut?.Dispose();
            _waveOut = null;
        }
    }

    private static Mp3Frame? LoadNextFrame(Stream audioStream, CancellationToken cancellationToken)
    {
        try
        {
            return Mp3Frame.LoadFromStream(audioStream);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private static Mp3WaveFormat CreateMp3WaveFormat(Mp3Frame frame)
    {
        var channels = frame.ChannelMode == ChannelMode.Mono ? 1 : 2;
        return new Mp3WaveFormat(frame.SampleRate, channels, frame.FrameLength, frame.BitRate);
    }

    private static void WaitForBufferSpace(
        BufferedWaveProvider bufferedProvider,
        int incomingByteCount,
        PlaybackStopState playbackState,
        CancellationToken cancellationToken)
    {
        while (bufferedProvider.BufferedBytes + incomingByteCount > bufferedProvider.BufferLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfPlaybackFailed(playbackState);
            Thread.Sleep(10);
        }
    }

    private static void WaitForBufferedPlaybackToDrain(
        BufferedWaveProvider bufferedProvider,
        WaveOutEvent waveOut,
        PlaybackStopState playbackState,
        CancellationToken cancellationToken)
    {
        while (bufferedProvider.BufferedBytes > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfPlaybackFailed(playbackState);
            Thread.Sleep(AmplitudeFrameDuration);
        }

        waveOut.Stop();
        WaitForPlaybackStopped(playbackState, cancellationToken);
    }

    private static void WaitForPlaybackStopped(PlaybackStopState playbackState, CancellationToken cancellationToken)
    {
        lock (playbackState)
        {
            while (!playbackState.IsStopped)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Monitor.Wait(playbackState, 10);
            }
        }

        ThrowIfPlaybackFailed(playbackState);
    }

    private static void ThrowIfPlaybackFailed(PlaybackStopState playbackState)
    {
        if (playbackState.Exception is not null)
        {
            throw playbackState.Exception;
        }
    }

    private static void ReportAmplitudeUntilComplete(
        ConcurrentQueue<double> amplitudeFrames,
        Action<double> mouthOpenChanged,
        Func<bool> isPlaybackComplete,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!isPlaybackComplete())
            {
                cancellationToken.ThrowIfCancellationRequested();
                mouthOpenChanged(amplitudeFrames.TryDequeue(out var nextOpenness) ? nextOpenness : 0);
                Thread.Sleep(AmplitudeFrameDuration);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void WaitForMouthTask(Task? mouthTask)
    {
        if (mouthTask is null)
        {
            return;
        }

        try
        {
            mouthTask.Wait();
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
        }
    }

    private static int BytesForDuration(WaveFormat waveFormat, TimeSpan duration)
    {
        var bytes = (int)Math.Round(waveFormat.AverageBytesPerSecond * duration.TotalSeconds);
        var blockAlign = Math.Max(1, waveFormat.BlockAlign);
        return Math.Max(blockAlign, bytes / blockAlign * blockAlign);
    }

    private static void TryDispose(Stream stream)
    {
        try
        {
            stream.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private sealed class PlaybackStopState
    {
        public bool IsStopped { get; set; }

        public Exception? Exception { get; set; }
    }

    private sealed class DecodedAmplitudeAnalyzer
    {
        private readonly byte[] _frameBuffer;
        private int _frameByteCount;
        private double _smoothed;

        public DecodedAmplitudeAnalyzer(int sampleRate, int bitsPerSample, int channels)
        {
            var blockAlign = channels * bitsPerSample / 8;
            var samplesPerFrame = Math.Max(1, (int)Math.Round(sampleRate * AmplitudeFrameDuration.TotalSeconds));
            _frameBuffer = new byte[Math.Max(blockAlign, samplesPerFrame * blockAlign)];
        }

        public void AddDecodedSamples(ReadOnlySpan<byte> samples, ConcurrentQueue<double> amplitudeFrames)
        {
            while (!samples.IsEmpty)
            {
                var bytesToCopy = Math.Min(samples.Length, _frameBuffer.Length - _frameByteCount);
                samples[..bytesToCopy].CopyTo(_frameBuffer.AsSpan(_frameByteCount));
                _frameByteCount += bytesToCopy;
                samples = samples[bytesToCopy..];

                if (_frameByteCount == _frameBuffer.Length)
                {
                    EnqueueFrame(_frameBuffer, amplitudeFrames);
                    _frameByteCount = 0;
                }
            }
        }

        public void Flush(ConcurrentQueue<double> amplitudeFrames)
        {
            if (_frameByteCount == 0)
            {
                return;
            }

            EnqueueFrame(_frameBuffer.AsSpan(0, _frameByteCount), amplitudeFrames);
            _frameByteCount = 0;
        }

        private void EnqueueFrame(ReadOnlySpan<byte> sampleFrame, ConcurrentQueue<double> amplitudeFrames)
        {
            var sampleCount = sampleFrame.Length / 2;
            if (sampleCount == 0)
            {
                return;
            }

            double sumSquares = 0;
            for (var byteIndex = 0; byteIndex + 1 < sampleFrame.Length; byteIndex += 2)
            {
                var sample = BinaryPrimitives.ReadInt16LittleEndian(sampleFrame[byteIndex..(byteIndex + 2)]);
                var normalizedSample = sample / 32768.0;
                sumSquares += normalizedSample * normalizedSample;
            }

            var rms = Math.Sqrt(sumSquares / sampleCount);
            var gated = rms < NoiseGate ? 0 : (rms - NoiseGate) / (1 - NoiseGate);
            var smoothing = gated > _smoothed ? Attack : Release;
            _smoothed += (gated - _smoothed) * smoothing;
            amplitudeFrames.Enqueue(Math.Clamp(_smoothed, 0, 1));
        }
    }
}
