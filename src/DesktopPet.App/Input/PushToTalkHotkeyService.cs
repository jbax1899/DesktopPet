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
    private const int VkControl = 0x11;
    private const int VkLcontrol = 0xA2;
    private const int VkRcontrol = 0xA3;
    private const int VkShift = 0x10;
    private const int VkLshift = 0xA0;
    private const int VkRshift = 0xA1;
    private const int VkMenu = 0x12;
    private const int VkLmenu = 0xA4;
    private const int VkRmenu = 0xA5;
    private const int VkLwin = 0x5B;
    private const int VkRwin = 0x5C;
    private const int VkSpace = 0x20;

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

    public PushToTalkHotkeyService(Action onKeyPressed, Action onKeyReleased)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _onKeyPressed = onKeyPressed;
        _onKeyReleased = onKeyReleased;
        _hookProc = OnHookCallback;
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

        if (vkCode == VkSpace)
        {
            TryFirePress();
        }
    }

    private void HandleKeyUp(int vkCode)
    {
        if (vkCode == VkSpace)
        {
            if (_firedPress)
            {
                _firedPress = false;
                _dispatcher.BeginInvoke(_onKeyReleased);
            }
        }

        UpdateModifierState(vkCode, held: false);
    }

    private void TryFirePress()
    {
        if (_ctrlHeld && !_shiftHeld && !_altHeld && !_winHeld && !_firedPress)
        {
            _firedPress = true;
            _dispatcher.BeginInvoke(_onKeyPressed);
        }
    }

    private void UpdateModifierState(int vkCode, bool held)
    {
        switch (vkCode)
        {
            case VkControl or VkLcontrol or VkRcontrol:
                _ctrlHeld = held;
                break;
            case VkShift or VkLshift or VkRshift:
                _shiftHeld = held;
                break;
            case VkMenu or VkLmenu or VkRmenu:
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
