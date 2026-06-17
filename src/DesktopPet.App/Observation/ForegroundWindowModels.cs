namespace DesktopPet.App.Observation;

internal readonly record struct WindowBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);

    public int Height => Math.Max(0, Bottom - Top);
}

internal sealed record ForegroundWindowSnapshot(
    nint WindowHandle,
    int ProcessId,
    string ProcessName,
    string ExecutablePath,
    string WindowTitle,
    WindowBounds Bounds,
    bool IsVisible,
    bool IsMinimized,
    DateTimeOffset ObservedAt);

internal interface IForegroundWindowCollector
{
    ForegroundWindowSnapshot? CollectPermittedMetadata();
}
