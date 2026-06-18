using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace DesktopPet.App.Memory;

public partial class ThumbnailBox : WpfUserControl
{
    public static readonly DependencyProperty ThumbnailPathProperty =
        DependencyProperty.Register(
            nameof(ThumbnailPath),
            typeof(string),
            typeof(ThumbnailBox),
            new PropertyMetadata(null, OnThumbnailPathChanged));

    public ThumbnailBox()
    {
        InitializeComponent();
        ThumbnailBorder.MouseLeftButtonDown += OnThumbnailClick;
        ExpandedPopup.MouseLeftButtonDown += OnExpandedPopupClick;
    }

    public string? ThumbnailPath
    {
        get => (string?)GetValue(ThumbnailPathProperty);
        set => SetValue(ThumbnailPathProperty, value);
    }

    private static void OnThumbnailPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ThumbnailBox)d;
        if (e.NewValue is string path && File.Exists(path))
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            control.ThumbnailImage.Source = bitmap;

            var expanded = new BitmapImage();
            expanded.BeginInit();
            expanded.UriSource = new Uri(path, UriKind.Absolute);
            expanded.CacheOption = BitmapCacheOption.OnLoad;
            expanded.EndInit();
            expanded.Freeze();
            control.ExpandedImage.Source = expanded;

            control.Visibility = Visibility.Visible;
        }
        else
        {
            control.Visibility = Visibility.Collapsed;
        }
    }

    private void OnThumbnailClick(object sender, MouseButtonEventArgs e)
    {
        if (ThumbnailImage.Source is null)
        {
            return;
        }

        ExpandedPopup.IsOpen = !ExpandedPopup.IsOpen;
        e.Handled = true;
    }

    private void OnExpandedPopupClick(object sender, MouseButtonEventArgs e)
    {
        ExpandedPopup.IsOpen = false;
        e.Handled = true;
    }
}
