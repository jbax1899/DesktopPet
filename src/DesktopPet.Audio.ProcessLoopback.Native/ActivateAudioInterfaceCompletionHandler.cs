using System.Runtime.InteropServices;
using NAudio.CoreAudioApi.Interfaces;

namespace DesktopPet.Audio.ProcessLoopback.Native;

// COM callback invoked by Windows when ActivateAudioInterfaceAsync completes.
// Receives the IAudioClient interface and hands it off to the awaiting Task.
//
// TODO: Remove once NAudio 3.x ships its own completion handler for process loopback.
internal sealed class ActivateAudioInterfaceCompletionHandler :
    IActivateAudioInterfaceCompletionHandler,
    IAgileObject
{
    private readonly TaskCompletionSource<IAudioClient> _tcs = new();

    public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
    {
        activateOperation.GetActivateResult(out int hr, out object punkAudioInterface);
        if (hr != 0)
        {
            _tcs.TrySetException(Marshal.GetExceptionForHR(hr, new IntPtr(-1)) ?? new InvalidOperationException("Activation failed."));
            return;
        }

        _tcs.TrySetResult((IAudioClient)punkAudioInterface);
    }

    public Task<IAudioClient> AwaitActivation() => _tcs.Task;
}
