using System.Windows;
using System.Windows.Media.Imaging;

namespace DesktopPet.App.Overlay;

public sealed record InochiPuppet(
    BitmapSource TextureAtlas,
    double Width,
    double Height,
    IReadOnlyList<InochiPart> Parts);

public sealed record InochiPart(
    string Name,
    Int32Rect SourceRect,
    Rect Bounds,
    double ZSort);
