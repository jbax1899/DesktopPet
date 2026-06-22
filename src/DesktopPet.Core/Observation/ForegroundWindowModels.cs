namespace DesktopPet.App.Observation;

public readonly record struct WindowBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);

    public int Height => Math.Max(0, Bottom - Top);
}

public sealed record ForegroundWindowSnapshot(
    nint WindowHandle,
    int ProcessId,
    string ProcessName,
    string ExecutablePath,
    string WindowTitle,
    WindowBounds Bounds,
    bool IsVisible,
    bool IsMinimized,
    DateTimeOffset ObservedAt);

public interface IForegroundWindowCollector
{
    ForegroundWindowSnapshot? CollectPermittedMetadata();
}
