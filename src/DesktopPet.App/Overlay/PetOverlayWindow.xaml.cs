using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace DesktopPet.App.Overlay;

public partial class PetOverlayWindow : Window
{
    private const int ExtendedWindowStyleIndex = -20; // GWL_EXSTYLE (Read or write the window's extended styles)
    private const int TransparentWindowStyle = 0x00000020; // WS_EX_TRANSPARENT (Makes the window transparent)
    private const double EdgeMargin = 32;

    private bool _isClickThrough;

    public PetOverlayWindow()
    {
        InitializeComponent();

        Loaded += (_, _) => MoveNearBottomRight();
    }

    public void SetClickThrough(bool isClickThrough)
    {
        _isClickThrough = isClickThrough;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        var currentStyle = GetWindowLongPtr(handle, ExtendedWindowStyleIndex);
        var updatedStyle = isClickThrough
            ? currentStyle | TransparentWindowStyle
            : currentStyle & ~TransparentWindowStyle;

        SetWindowLongPtr(handle, ExtendedWindowStyleIndex, updatedStyle);
    }

    public void ShowNearBottomRight()
    {
        MoveNearBottomRight();

        Show();
        Activate();
    }

    private void OnOverlayMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThrough || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        DragMove();
        KeepInsideWorkArea();
    }

    private void MoveNearBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - EdgeMargin;
        Top = workArea.Bottom - Height - EdgeMargin;
    }

    private void KeepInsideWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        var windowWidth = GetWindowWidth();
        var windowHeight = GetWindowHeight();

        Left = Clamp(Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - windowWidth));
        Top = Clamp(Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - windowHeight));
    }

    private double GetWindowWidth()
    {
        return ActualWidth > 0 ? ActualWidth : Width;
    }

    private double GetWindowHeight()
    {
        return ActualHeight > 0 ? ActualHeight : Height;
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        return Math.Min(Math.Max(value, minimum), maximum);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);
}
