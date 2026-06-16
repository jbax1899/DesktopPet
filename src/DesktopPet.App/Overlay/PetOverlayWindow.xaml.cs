using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace DesktopPet.App.Overlay;

public partial class PetOverlayWindow : Window
{
    private const int ExtendedWindowStyleIndex = -20;
    private const int TransparentWindowStyle = 0x00000020;

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

    private void OnOverlayMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThrough || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        DragMove();
    }

    private void MoveNearBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 32;
        Top = workArea.Bottom - Height - 32;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);
}
