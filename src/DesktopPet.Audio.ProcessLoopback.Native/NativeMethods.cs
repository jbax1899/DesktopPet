using System.Runtime.InteropServices;

namespace DesktopPet.Audio.ProcessLoopback.Native;

// P/Invoke declarations for ActivateAudioInterfaceAsync from Mmdevapi.dll.
// Based on Microsoft's ApplicationLoopback sample:
// https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/
//
// TODO: Remove this file entirely once NAudio 3.x ships process loopback support
// (PR #1225 / WasapiCapture.CreateForProcessCaptureAsync).
internal static class NativeMethods
{
    internal const string VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK = "VAD\\Process_Loopback";

    // https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-activateaudiointerfaceasync
    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    internal static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        ref Guid riid,
        IntPtr activationParams,  // pointer to PROPVARIANT
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);
}

// IActivateAudioInterfaceAsyncOperation — retrieves activation results.
[ComImport, Guid("4F03D005-7DCF-491b-9F46-7FEDE1201960")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceAsyncOperation
{
    void GetActivateResult(
        [MarshalAs(UnmanagedType.Error)] out int hr,
        [MarshalAs(UnmanagedType.IUnknown)] out object activationInterface);
}

// IActivateAudioInterfaceCompletionHandler — callback when activation completes.
[ComImport, Guid("41B89B61-89AB-48E1-B3F3-18DF12013CE3")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceCompletionHandler
{
    void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
}

// IAgileObject — required for COM MTA callback.
[ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAgileObject
{
}
