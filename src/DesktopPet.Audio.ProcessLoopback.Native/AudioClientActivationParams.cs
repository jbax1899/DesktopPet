using System.Runtime.InteropServices;

namespace DesktopPet.Audio.ProcessLoopback.Native;

// AUDIOCLIENT_ACTIVATION_PARAMS — specifies activation type for ActivateAudioInterfaceAsync.
// https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ns-audioclientactivationparams-audioclient_activation_params
[StructLayout(LayoutKind.Sequential)]
internal struct AudioClientActivationParams
{
    public AudioClientActivationType ActivationType;
    public AudioClientProcessLoopbackParams ProcessLoopbackParams;
}

// AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS — target PID and include/exclude mode.
// https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ns-audioclientactivationparams-audioclient_process_loopback_params
[StructLayout(LayoutKind.Sequential)]
internal struct AudioClientProcessLoopbackParams
{
    public uint TargetProcessId;
    public ProcessLoopbackMode ProcessLoopbackMode;
}

// AUDIOCLIENT_ACTIVATION_TYPE
internal enum AudioClientActivationType : int
{
    Default = 0,
    ProcessLoopback = 1
}

// PROCESS_LOOPBACK_MODE
internal enum ProcessLoopbackMode : int
{
    IncludeTargetProcessTree = 0,
    ExcludeTargetProcessTree = 1
}
