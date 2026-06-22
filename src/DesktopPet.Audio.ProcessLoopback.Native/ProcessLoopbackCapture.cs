using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DesktopPet.Audio.ProcessLoopback.Native;

// Thin managed wrapper around Microsoft's ApplicationLoopback sample.
// Exposes a minimal Start/Stop API that delivers raw PCM16 stereo 44100 Hz
// frames via a callback on a background thread.
//
// Under the hood: ActivateAudioInterfaceAsync + AUDIOCLIENT_ACTIVATION_PARAMS
// with AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK. NAudio handles the
// IAudioClient/IAudioCaptureClient COM plumbing and buffer management.
//
// TODO: Replace with NAudio's built-in process loopback once NAudio 3.x ships
// support (PR #1225 / WasapiCapture.CreateForProcessCaptureAsync). At that
// point this entire class can be deleted and replaced with ~5 lines of NAudio calls.
public static class ProcessLoopbackCapture
{
    private static AudioClient? _audioClient;
    private static Thread? _captureThread;
    private static int _capturing;

    public static bool IsSupported()
    {
        return Environment.OSVersion.Version.Build >= 19041;
    }

    public static async Task StartAsync(
        int processId,
        bool includeProcessTree,
        Action<byte[], int, int> onPcmFrame,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupported())
            throw new PlatformNotSupportedException(
                "Process loopback capture requires Windows 10 build 19041 or later.");
        if (Interlocked.CompareExchange(ref _capturing, 1, 0) != 0)
            throw new InvalidOperationException("A process loopback capture is already active.");

        var audioClientInterface = await ActivateAudioClientAsync(processId, includeProcessTree);

        _audioClient = new AudioClient(audioClientInterface);
        _audioClient.Initialize(
            AudioClientShareMode.Shared,
            AudioClientStreamFlags.AutoConvertPcm
                | AudioClientStreamFlags.Loopback
                | AudioClientStreamFlags.EventCallback,
            0, 0,
            new WaveFormat(44100, 16, 2),
            Guid.Empty);

        _captureThread = new Thread(() => CaptureLoop(_audioClient, onPcmFrame, cancellationToken))
        {
            IsBackground = true,
            Name = "ProcessLoopbackCapture"
        };
        _captureThread.Start();
    }

    public static void Stop()
    {
        if (Interlocked.Exchange(ref _capturing, 0) == 0)
            return;

        try { _audioClient?.Stop(); } catch { /* best effort */ }

        _captureThread?.Join(5000);
        _captureThread = null;

        if (_audioClient is not null)
        {
            try { _audioClient.Dispose(); } catch { /* best effort */ }
            _audioClient = null;
        }
    }

    private static async Task<NAudio.CoreAudioApi.Interfaces.IAudioClient> ActivateAudioClientAsync(
        int processId, bool includeProcessTree)
    {
        var icbh = new ActivateAudioInterfaceCompletionHandler();

        var activationParams = new AudioClientActivationParams
        {
            ActivationType = AudioClientActivationType.ProcessLoopback,
            ProcessLoopbackParams = new AudioClientProcessLoopbackParams
            {
                TargetProcessId = (uint)processId,
                ProcessLoopbackMode = includeProcessTree
                    ? ProcessLoopbackMode.IncludeTargetProcessTree
                    : ProcessLoopbackMode.ExcludeTargetProcessTree
            }
        };

        int paramSize = Marshal.SizeOf<AudioClientActivationParams>();
        nint paramBlob = Marshal.AllocHGlobal(paramSize);
        nint propVariant = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.StructureToPtr(activationParams, paramBlob, false);

            // PROPVARIANT: vt=VT_BLOB(0x41), cbSize, pBlobData
            Marshal.WriteInt16(propVariant, 0, 0x0041);
            Marshal.WriteInt32(propVariant, 8, paramSize);
            Marshal.WriteIntPtr(propVariant, 16, paramBlob);

            var iid = typeof(NAudio.CoreAudioApi.Interfaces.IAudioClient).GUID;
            NativeMethods.ActivateAudioInterfaceAsync(
                NativeMethods.VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,
                ref iid,
                propVariant,
                icbh,
                out _);
        }
        finally
        {
            Marshal.FreeHGlobal(propVariant);
            Marshal.FreeHGlobal(paramBlob);
        }

        return await icbh.AwaitActivation();
    }

    private static void CaptureLoop(
        AudioClient audioClient,
        Action<byte[], int, int> onPcmFrame,
        CancellationToken ct)
    {
        var captureClient = audioClient.AudioCaptureClient;

        int bufferFrameCount = audioClient.BufferSize;
        int bytesPerFrame = 4; // PCM16 stereo = 2 channels * 2 bytes
        int bufferByteSize = bufferFrameCount * bytesPerFrame;
        byte[] recordBuffer = new byte[bufferByteSize];

        audioClient.Start();

        try
        {
            while (!ct.IsCancellationRequested && Volatile.Read(ref _capturing) != 0)
            {
                try
                {
                    int packetSize = captureClient.GetNextPacketSize();
                    int recordBufferOffset = 0;

                    while (packetSize > 0)
                    {
                        nint buffer = captureClient.GetBuffer(
                            out int framesAvailable,
                            out AudioClientBufferFlags flags);

                        int bytesAvailable = framesAvailable * bytesPerFrame;

                        if (recordBufferOffset + bytesAvailable > bufferByteSize && recordBufferOffset > 0)
                        {
                            onPcmFrame(recordBuffer, 0, recordBufferOffset);
                            recordBufferOffset = 0;
                        }

                        if ((flags & AudioClientBufferFlags.Silent) != AudioClientBufferFlags.Silent)
                        {
                            Marshal.Copy(buffer, recordBuffer, recordBufferOffset, bytesAvailable);
                        }
                        else
                        {
                            Array.Clear(recordBuffer, recordBufferOffset, bytesAvailable);
                        }

                        recordBufferOffset += bytesAvailable;
                        captureClient.ReleaseBuffer(framesAvailable);
                        packetSize = captureClient.GetNextPacketSize();
                    }

                    if (recordBufferOffset > 0)
                    {
                        onPcmFrame(recordBuffer, 0, recordBufferOffset);
                    }
                }
                catch (COMException)
                {
                    // Device disconnected or format changed.
                    break;
                }

                // Brief sleep to avoid tight-looping when no packets are available.
                Thread.Sleep(1);
            }
        }
        finally
        {
            try { audioClient.Stop(); } catch { /* best effort */ }
        }
    }
}
