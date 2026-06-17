using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using NAudio.Wave;

namespace DesktopPet.App.Voice;

public sealed class StreamingPcmAudioPlayer : IDisposable
{
    private static readonly TimeSpan PlaybackStartBufferDuration = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan AmplitudeFrameDuration = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan BufferDuration = TimeSpan.FromSeconds(5);
    private const double NoiseGate = 0.08;
    private const double Attack = 0.75;
    private const double Release = 0.35;

    private WaveOutEvent? _waveOut;

    public async Task PlayAsync(
        Stream audioStream,
        string audioFormat,
        int sampleRate,
        int bitsPerSample,
        int channels,
        CancellationToken cancellationToken,
        Action<double>? mouthOpenChanged = null)
    {
        if (!string.Equals(audioFormat, "pcm_s16le", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported audio format: {audioFormat}");
        }

        if (sampleRate <= 0 || bitsPerSample != 16 || channels != 1)
        {
            throw new InvalidOperationException($"Unsupported PCM shape: {sampleRate} Hz, {bitsPerSample}-bit, {channels} channel(s).");
        }

        StopAndClearCurrentPlayback();

        var waveFormat = new WaveFormat(sampleRate, bitsPerSample, channels);
        var bufferedProvider = new BufferedWaveProvider(waveFormat)
        {
            BufferDuration = BufferDuration,
            DiscardOnBufferOverflow = false
        };
        using var waveOut = new WaveOutEvent();
        _waveOut = waveOut;

        var waveOutStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var playbackTimingComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var amplitudeFrames = new ConcurrentQueue<double>();
        var amplitudeAnalyzer = mouthOpenChanged is null
            ? null
            : new PcmAmplitudeAnalyzer(sampleRate, bitsPerSample, channels);
        Task? mouthTask = null;
        var playbackStarted = false;
        var readBuffer = ArrayPool<byte>.Shared.Rent(8192);

        waveOut.PlaybackStopped += OnPlaybackStopped;
        waveOut.Init(bufferedProvider);

        try
        {
            var startThresholdBytes = BytesForDuration(waveFormat, PlaybackStartBufferDuration);

            while (true)
            {
                var bytesRead = await audioStream.ReadAsync(readBuffer.AsMemory(0, readBuffer.Length), cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                await WaitForBufferSpaceAsync(bufferedProvider, bytesRead, waveOutStopped.Task, cancellationToken);
                bufferedProvider.AddSamples(readBuffer, 0, bytesRead);
                amplitudeAnalyzer?.AddPcm(readBuffer.AsSpan(0, bytesRead), amplitudeFrames);

                if (!playbackStarted && bufferedProvider.BufferedBytes >= startThresholdBytes)
                {
                    StartPlayback();
                }
            }

            amplitudeAnalyzer?.Flush(amplitudeFrames);

            if (!playbackStarted && bufferedProvider.BufferedBytes > 0)
            {
                StartPlayback();
            }

            if (playbackStarted)
            {
                await WaitForBufferedPlaybackToDrainAsync(bufferedProvider, waveOut, waveOutStopped.Task, cancellationToken);
            }
        }
        finally
        {
            playbackTimingComplete.TrySetResult();
            if (mouthTask is not null)
            {
                await IgnoreCancellationAsync(mouthTask);
            }

            waveOut.PlaybackStopped -= OnPlaybackStopped;
            mouthOpenChanged?.Invoke(0);
            ArrayPool<byte>.Shared.Return(readBuffer);
            StopAndClearCurrentPlayback();
        }

        return;

        void StartPlayback()
        {
            playbackStarted = true;
            waveOut.Play();

            if (mouthOpenChanged is not null)
            {
                mouthTask = ReportAmplitudeUntilCompleteAsync(
                    amplitudeFrames,
                    mouthOpenChanged,
                    playbackTimingComplete.Task,
                    cancellationToken);
            }
        }

        void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception is null)
            {
                waveOutStopped.TrySetResult();
            }
            else
            {
                waveOutStopped.TrySetException(e.Exception);
            }
        }
    }

    public void Dispose()
    {
        StopAndClearCurrentPlayback();
    }

    private void StopAndClearCurrentPlayback()
    {
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;
    }

    private static async Task ReportAmplitudeUntilCompleteAsync(
        ConcurrentQueue<double> amplitudeFrames,
        Action<double>? mouthOpenChanged,
        Task playbackTask,
        CancellationToken cancellationToken)
    {
        if (mouthOpenChanged is null)
        {
            await playbackTask.WaitAsync(cancellationToken);
            return;
        }

        var frameDelay = AmplitudeFrameDuration;
        while (!playbackTask.IsCompleted)
        {
            var openness = amplitudeFrames.TryDequeue(out var nextOpenness) ? nextOpenness : 0;
            mouthOpenChanged(openness);

            var delayTask = Task.Delay(frameDelay, cancellationToken);
            var completedTask = await Task.WhenAny(playbackTask, delayTask);
            if (completedTask == playbackTask)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        await playbackTask.WaitAsync(cancellationToken);
    }

    private static async Task WaitForBufferSpaceAsync(
        BufferedWaveProvider bufferedProvider,
        int incomingByteCount,
        Task playbackStoppedTask,
        CancellationToken cancellationToken)
    {
        while (bufferedProvider.BufferedBytes + incomingByteCount > bufferedProvider.BufferLength)
        {
            if (playbackStoppedTask.IsCompleted)
            {
                await playbackStoppedTask;
            }

            await Task.Delay(10, cancellationToken);
        }
    }

    private static async Task WaitForBufferedPlaybackToDrainAsync(
        BufferedWaveProvider bufferedProvider,
        WaveOutEvent waveOut,
        Task playbackStoppedTask,
        CancellationToken cancellationToken)
    {
        while (bufferedProvider.BufferedBytes > 0)
        {
            if (playbackStoppedTask.IsCompleted)
            {
                await playbackStoppedTask;
            }

            await Task.Delay(AmplitudeFrameDuration, cancellationToken);
        }

        waveOut.Stop();
        await playbackStoppedTask.WaitAsync(cancellationToken);
    }

    private static int BytesForDuration(WaveFormat waveFormat, TimeSpan duration)
    {
        var bytes = (int)Math.Round(waveFormat.AverageBytesPerSecond * duration.TotalSeconds);
        var blockAlign = Math.Max(1, waveFormat.BlockAlign);
        return Math.Max(blockAlign, bytes / blockAlign * blockAlign);
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class PcmAmplitudeAnalyzer
    {
        private readonly byte[] _frameBuffer;
        private int _frameByteCount;
        private double _smoothed;

        public PcmAmplitudeAnalyzer(int sampleRate, int bitsPerSample, int channels)
        {
            var blockAlign = channels * bitsPerSample / 8;
            var samplesPerFrame = Math.Max(1, (int)Math.Round(sampleRate * AmplitudeFrameDuration.TotalSeconds));
            _frameBuffer = new byte[Math.Max(blockAlign, samplesPerFrame * blockAlign)];
        }

        public void AddPcm(ReadOnlySpan<byte> pcm, ConcurrentQueue<double> amplitudeFrames)
        {
            while (!pcm.IsEmpty)
            {
                var bytesToCopy = Math.Min(pcm.Length, _frameBuffer.Length - _frameByteCount);
                pcm[..bytesToCopy].CopyTo(_frameBuffer.AsSpan(_frameByteCount));
                _frameByteCount += bytesToCopy;
                pcm = pcm[bytesToCopy..];

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

        private void EnqueueFrame(ReadOnlySpan<byte> pcmFrame, ConcurrentQueue<double> amplitudeFrames)
        {
            var sampleCount = pcmFrame.Length / 2;
            if (sampleCount == 0)
            {
                return;
            }

            double sumSquares = 0;
            for (var byteIndex = 0; byteIndex + 1 < pcmFrame.Length; byteIndex += 2)
            {
                var sample = BinaryPrimitives.ReadInt16LittleEndian(pcmFrame[byteIndex..(byteIndex + 2)]);
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
