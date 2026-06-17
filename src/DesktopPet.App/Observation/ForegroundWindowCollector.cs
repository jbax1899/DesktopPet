using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopPet.App.Observation;

internal sealed partial class ForegroundWindowCollector : IForegroundWindowCollector
{
    private readonly IObservationPermissionService _permissionService;

    public ForegroundWindowCollector(IObservationPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    public ForegroundWindowSnapshot? CollectPermittedMetadata()
    {
        var windowHandle = NativeMethods.GetForegroundWindow();
        if (windowHandle == nint.Zero || !NativeMethods.IsWindow(windowHandle))
        {
            return null;
        }

        _ = NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
        if (processId == 0 || processId == Environment.ProcessId)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById(unchecked((int)processId));
            var executablePath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath)
                || !_permissionService.IsAllowed(executablePath, DesktopContextCapabilities.Metadata))
            {
                return null;
            }

            if (!NativeMethods.GetWindowRect(windowHandle, out var rect))
            {
                return null;
            }

            return new ForegroundWindowSnapshot(
                windowHandle,
                process.Id,
                process.ProcessName,
                ObservationApplicationIdentity.NormalizePath(executablePath),
                ReadWindowTitle(windowHandle),
                new WindowBounds(rect.Left, rect.Top, rect.Right, rect.Bottom),
                NativeMethods.IsWindowVisible(windowHandle),
                NativeMethods.IsIconic(windowHandle),
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is ArgumentException
            or InvalidOperationException
            or Win32Exception
            or NotSupportedException)
        {
            return null;
        }
    }

    private static unsafe string ReadWindowTitle(nint windowHandle)
    {
        var length = Math.Clamp(NativeMethods.GetWindowTextLengthW(windowHandle), 0, 512);
        if (length == 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        fixed (char* pointer = buffer)
        {
            var copied = NativeMethods.GetWindowTextW(windowHandle, pointer, buffer.Length);
            return copied <= 0 ? string.Empty : new string(buffer, 0, copied);
        }
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        internal static partial nint GetForegroundWindow();

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsWindow(nint windowHandle);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsWindowVisible(nint windowHandle);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsIconic(nint windowHandle);

        [LibraryImport("user32.dll")]
        internal static partial uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetWindowRect(nint windowHandle, out NativeRect rect);

        [LibraryImport("user32.dll")]
        internal static partial int GetWindowTextLengthW(nint windowHandle);

        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static unsafe partial int GetWindowTextW(nint windowHandle, char* text, int maximumCount);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
