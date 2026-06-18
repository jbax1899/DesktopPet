using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace DesktopPet.App.Observation;

public sealed class CapturedWindowImage : IDisposable
{
    public CapturedWindowImage(Bitmap bitmap)
    {
        Bitmap = bitmap;
    }

    public Bitmap Bitmap { get; }

    public void Dispose()
    {
        Bitmap.Dispose();
    }
}

public interface IWindowCaptureService
{
    Task<(DesktopContextCollectionStatus Status, CapturedWindowImage? Image)> CaptureAsync(
        nint windowHandle,
        string executablePath,
        bool isVisible,
        bool isMinimized,
        CancellationToken cancellationToken);
}

internal sealed partial class WindowCaptureService : IWindowCaptureService
{
    private const uint PrintWindowRenderFullContent = 0x00000002;

    private readonly IObservationPermissionService _permissionService;

    public WindowCaptureService(IObservationPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    public Task<(DesktopContextCollectionStatus Status, CapturedWindowImage? Image)> CaptureAsync(
        nint windowHandle,
        string executablePath,
        bool isVisible,
        bool isMinimized,
        CancellationToken cancellationToken)
    {
        if (!_permissionService.IsAllowed(executablePath, DesktopContextCapabilities.Visual))
        {
            return Task.FromResult<(DesktopContextCollectionStatus, CapturedWindowImage?)>(
                (DesktopContextCollectionStatus.NotPermitted, null));
        }

        if (!isVisible || isMinimized || windowHandle == nint.Zero || !NativeMethods.IsWindow(windowHandle))
        {
            return Task.FromResult<(DesktopContextCollectionStatus, CapturedWindowImage?)>(
                (DesktopContextCollectionStatus.Unavailable, null));
        }

        var settings = _permissionService.Current;
        return Task.Run(
            () => Capture(
                windowHandle,
                settings.MaximumScreenshotWidth,
                settings.MaximumScreenshotHeight,
                cancellationToken),
            cancellationToken);
    }

    private static (DesktopContextCollectionStatus, CapturedWindowImage?) Capture(
        nint windowHandle,
        int maximumWidth,
        int maximumHeight,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeMethods.GetWindowRect(windowHandle, out var bounds))
        {
            return (DesktopContextCollectionStatus.Unavailable, null);
        }

        var width = Math.Max(0, bounds.Right - bounds.Left);
        var height = Math.Max(0, bounds.Bottom - bounds.Top);
        if (width == 0 || height == 0)
        {
            return (DesktopContextCollectionStatus.Unavailable, null);
        }

        using var source = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(source))
        {
            var deviceContext = graphics.GetHdc();
            try
            {
                if (!NativeMethods.PrintWindow(windowHandle, deviceContext, PrintWindowRenderFullContent))
                {
                    return (DesktopContextCollectionStatus.Unsupported, null);
                }
            }
            finally
            {
                graphics.ReleaseHdc(deviceContext);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var scale = Math.Min(1d, Math.Min((double)maximumWidth / width, (double)maximumHeight / height));
        var outputWidth = Math.Max(1, (int)Math.Round(width * scale));
        var outputHeight = Math.Max(1, (int)Math.Round(height * scale));
        var output = new Bitmap(outputWidth, outputHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.CompositingQuality = CompositingQuality.HighSpeed;
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(source, 0, 0, outputWidth, outputHeight);
        }

        return (DesktopContextCollectionStatus.Available, new CapturedWindowImage(output));
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsWindow(nint windowHandle);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetWindowRect(nint windowHandle, out NativeRect bounds);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool PrintWindow(nint windowHandle, nint deviceContext, uint flags);
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
