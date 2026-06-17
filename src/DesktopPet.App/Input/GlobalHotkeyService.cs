using DesktopPet.App.Settings;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace DesktopPet.App.Input;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x4450;
    private const int WindowsHotkeyMessage = 0x0312; // WM_HOTKEY
    private const uint ModifierAlt = 0x0001;
    private const uint ModifierControl = 0x0002;
    private const uint ModifierShift = 0x0004;
    private const uint ModifierWindows = 0x0008;
    private const uint ModifierNoRepeat = 0x4000;

    private readonly Window _messageWindow;
    private readonly Action _onHotkeyPressed;
    private HwndSource? _source;
    private nint _handle;
    private bool _isHooked;
    private bool _isRegistered;

    public GlobalHotkeyService(Window messageWindow, Action onHotkeyPressed)
    {
        _messageWindow = messageWindow;
        _onHotkeyPressed = onHotkeyPressed;
    }

    public string? CurrentRegistrationError { get; private set; }

    public string? Register(KeyboardShortcut shortcut)
    {
        Unregister();
        CurrentRegistrationError = null;

        if (!shortcut.IsValid() || !shortcut.TryGetWpfKey(out var key))
        {
            CurrentRegistrationError = "Shortcut must include at least one modifier and one non-modifier key.";
            return CurrentRegistrationError;
        }

        EnsureHook();
        if (_handle == nint.Zero)
        {
            CurrentRegistrationError = "The app window is not ready for hotkey registration.";
            return CurrentRegistrationError;
        }

        var modifiers = BuildModifiers(shortcut);
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey <= 0)
        {
            CurrentRegistrationError = $"The key {shortcut.Key} cannot be registered as a global hotkey.";
            return CurrentRegistrationError;
        }

        if (!RegisterHotKey(_handle, HotkeyId, modifiers, (uint)virtualKey))
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            CurrentRegistrationError = $"Could not register {shortcut.DisplayText}: {error.Message}";
            return CurrentRegistrationError;
        }

        _isRegistered = true;
        return null;
    }

    public void Dispose()
    {
        Unregister();

        if (_source is not null && _isHooked)
        {
            _source.RemoveHook(OnWindowMessage);
            _isHooked = false;
        }
    }

    private void EnsureHook()
    {
        if (_isHooked)
        {
            return;
        }

        _handle = new WindowInteropHelper(_messageWindow).Handle;
        if (_handle == nint.Zero)
        {
            return;
        }

        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(OnWindowMessage);
        _isHooked = _source is not null;
    }

    private void Unregister()
    {
        if (!_isRegistered || _handle == nint.Zero)
        {
            _isRegistered = false;
            return;
        }

        UnregisterHotKey(_handle, HotkeyId);
        _isRegistered = false;
    }

    private nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WindowsHotkeyMessage && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _onHotkeyPressed();
        }

        return nint.Zero;
    }

    private static uint BuildModifiers(KeyboardShortcut shortcut)
    {
        var modifiers = ModifierNoRepeat;
        if (shortcut.Control)
        {
            modifiers |= ModifierControl;
        }

        if (shortcut.Shift)
        {
            modifiers |= ModifierShift;
        }

        if (shortcut.Alt)
        {
            modifiers |= ModifierAlt;
        }

        if (shortcut.Windows)
        {
            modifiers |= ModifierWindows;
        }

        return modifiers;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
