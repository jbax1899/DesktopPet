using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ControlsImage = System.Windows.Controls.Image;

namespace DesktopPet.App.Overlay;

public sealed class WpfInochiPuppetView : Canvas
{
    private readonly Dictionary<string, FrameworkElement> _parts = [];
    private readonly Dictionary<string, TranslateTransform> _partOffsets = [];
    private readonly Dictionary<string, Rect> _partBounds = [];
    private readonly ScaleTransform _rootScale = new(1, 1);
    private readonly TranslateTransform _rootOffset = new();

    public void Load(InochiPuppet puppet)
    {
        Children.Clear();
        _parts.Clear();
        _partOffsets.Clear();
        _partBounds.Clear();

        Width = puppet.Width;
        Height = puppet.Height;
        RenderTransformOrigin = new System.Windows.Point(0.5, 0.88);
        RenderTransform = new TransformGroup
        {
            Children =
            {
                _rootScale,
                _rootOffset
            }
        };

        foreach (var part in puppet.Parts)
        {
            var source = new CroppedBitmap(puppet.TextureAtlas, part.SourceRect);
            source.Freeze();

            var offset = new TranslateTransform();
            var image = new ControlsImage
            {
                Source = source,
                Width = part.Bounds.Width,
                Height = part.Bounds.Height,
                RenderTransform = offset
            };

            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            SetLeft(image, part.Bounds.X);
            SetTop(image, part.Bounds.Y);

            _parts[part.Name] = image;
            _partOffsets[part.Name] = offset;
            _partBounds[part.Name] = part.Bounds;
            Children.Add(image);
        }
    }

    public void SetRootPose(double offsetY, double scaleX, double scaleY)
    {
        _rootOffset.Y = offsetY;
        _rootScale.ScaleX = scaleX;
        _rootScale.ScaleY = scaleY;
    }

    public void SetPartVisible(string partName, bool isVisible)
    {
        if (_parts.TryGetValue(partName, out var part))
        {
            part.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public void SetPartOffset(string partName, double x, double y)
    {
        if (!_partOffsets.TryGetValue(partName, out var offset))
        {
            return;
        }

        offset.X = x;
        offset.Y = y;
    }

    public System.Windows.Point GetPartCenterScreenPoint(string partName, Vector localOffset)
    {
        if (!_partBounds.TryGetValue(partName, out var bounds))
        {
            return PointToScreen(new System.Windows.Point(ActualWidth / 2, ActualHeight / 2));
        }

        return PointToScreen(new System.Windows.Point(
            bounds.X + bounds.Width / 2 + localOffset.X,
            bounds.Y + bounds.Height / 2 + localOffset.Y));
    }
}
