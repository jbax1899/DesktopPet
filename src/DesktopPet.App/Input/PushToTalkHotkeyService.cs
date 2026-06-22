using DesktopPet.App.Settings;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace DesktopPet.App.Input;

public sealed class PushToTalkHotkeyService : IDisposable
{
    private const int WhKeyboardLowLevel = 13;
    private const int WmKeydown = 0x0100;
    private const int WmKeyup = 0x0101;
    private const int WmSyskeydown = 0x0104;
    private const int WmSyskeyup = 0x0105;
    private const int VkLwin = 0x5B;
    private const int VkRwin = 0x5C;

    private readonly Dispatcher _dispatcher;
    private readonly Action _onKeyPressed;
    private readonly Action _onKeyReleased;
    private readonly LowLevelKeyboardProc _hookProc;
    private nint _hookHandle;
    private bool _hookInstalled;
    private bool _ctrlHeld;
    private bool _shiftHeld;
    private bool _altHeld;
    private bool _winHeld;
    private bool _firedPress;
    private bool _disposed;
    private KeyboardShortcut _shortcut;
    private int _triggerVk;
    private bool _requireCtrl;
    private bool _requireShift;
    private bool _requireAlt;
    private bool _requireWin;

    public PushToTalkHotkeyService(
        KeyboardShortcut shortcut,
        Action onKeyPressed,
        Action onKeyReleased)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _onKeyPressed = onKeyPressed;
        _onKeyReleased = onKeyReleased;
        _hookProc = OnHookCallback;
        _shortcut = shortcut;
        ApplyShortcut(shortcut);
    }

    public void ApplyShortcut(KeyboardShortcut shortcut)
    {
        _shortcut = shortcut;
        _requireCtrl = shortcut.Control;
        _requireShift = shortcut.Shift;
        _requireAlt = shortcut.Alt;
        _requireWin = shortcut.Windows;
        _triggerVk = shortcut.TryGetWpfKey(out var key) ? KeyInterop.VirtualKeyFromKey(key) : 0;
        _firedPress = false;
    }

    public void EnsureHookInstalled()
    {
        if (_hookInstalled || _disposed)
        {
            return;
        }

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        if (module is null)
        {
            return;
        }

        var handle = GetModuleHandle(module.ModuleName);
        if (handle == nint.Zero)
        {
            return;
        }

        _hookHandle = SetWindowsHookEx(WhKeyboardLowLevel, _hookProc, handle, 0);
        _hookInstalled = _hookHandle != nint.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UninstallHook();
        _disposed = true;
    }

    private void UninstallHook()
    {
        if (_hookInstalled && _hookHandle != nint.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = nint.Zero;
            _hookInstalled = false;
        }
    }

    private nint OnHookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var msg = wParam.ToInt32();
        var vkCode = Marshal.ReadInt32(lParam);

        switch (msg)
        {
            case WmKeydown or WmSyskeydown:
                HandleKeyDown(vkCode);
                break;
            case WmKeyup or WmSyskeyup:
                HandleKeyUp(vkCode);
                break;
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void HandleKeyDown(int vkCode)
    {
        UpdateModifierState(vkCode, held: true);

        if (vkCode == _triggerVk && _triggerVk != 0)
        {
            TryFirePress();
        }
    }

    private void HandleKeyUp(int vkCode)
    {
        if (vkCode == _triggerVk && _firedPress)
        {
            _firedPress = false;
            _dispatcher.BeginInvoke(_onKeyReleased);
        }

        UpdateModifierState(vkCode, held: false);
    }

    private void TryFirePress()
    {
        if (_requireCtrl == _ctrlHeld
            && _requireShift == _shiftHeld
            && _requireAlt == _altHeld
            && _requireWin == _winHeld
            && !_firedPress)
        {
            _firedPress = true;
            _dispatcher.BeginInvoke(_onKeyPressed);
        }
    }

    private void UpdateModifierState(int vkCode, bool held)
    {
        switch (vkCode)
        {
            case 0x11 or 0xA2 or 0xA3:
                _ctrlHeld = held;
                break;
            case 0x10 or 0xA0 or 0xA1:
                _shiftHeld = held;
                break;
            case 0x12 or 0xA4 or 0xA5:
                _altHeld = held;
                break;
            case VkLwin or VkRwin:
                _winHeld = held;
                break;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string lpModuleName);
}
