using DesktopPet.App.Errors;
using DesktopPet.App.Settings;
using System.ComponentModel;
using System.Diagnostics;
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
    private const int ErrorHotkeyAlreadyRegistered = 1409;

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

    public PetError? CurrentRegistrationError { get; private set; }

    public PetError? Register(KeyboardShortcut shortcut)
    {
        Unregister();
        CurrentRegistrationError = null;

        if (!shortcut.IsValid() || !shortcut.TryGetWpfKey(out var key))
        {
            return SetRegistrationError(PetErrorCode.HotkeyInvalid, "Shortcut must include at least one modifier and one non-modifier key.");
        }

        EnsureHook();
        if (_handle == nint.Zero)
        {
            return SetRegistrationError(PetErrorCode.HotkeyInvalid, "The app window is not ready for hotkey registration.");
        }

        var modifiers = BuildModifiers(shortcut);
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey <= 0)
        {
            return SetRegistrationError(PetErrorCode.HotkeyInvalid, $"The key {shortcut.Key} cannot be registered as a global hotkey.");
        }

        if (!RegisterHotKey(_handle, HotkeyId, modifiers, (uint)virtualKey))
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            var code = error.NativeErrorCode == ErrorHotkeyAlreadyRegistered
                ? PetErrorCode.HotkeyConflict
                : PetErrorCode.HotkeyInvalid;
            return SetRegistrationError(code, $"Could not register {shortcut.DisplayText}: {error.Message}");
        }

        _isRegistered = true;
        return null;
    }

    private PetError SetRegistrationError(PetErrorCode code, string technicalMessage)
    {
        CurrentRegistrationError = new PetError(code, technicalMessage);
        Debug.WriteLine($"DesktopPet hotkey error ({code}): {technicalMessage}");
        return CurrentRegistrationError;
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
