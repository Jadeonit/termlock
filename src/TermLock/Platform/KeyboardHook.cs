using System.Runtime.InteropServices;
using TermLock.Core;

namespace TermLock.Platform;

/// <summary>
/// Global low-level keyboard hook (WH_KEYBOARD_LL).
///
/// While the curtain is visible it swallows the keys that would let a casual
/// user escape it: Win, Alt+Tab, Alt+F4, Ctrl+Esc and PrintScreen.
/// It does NOT and cannot block Ctrl+Alt+Del.
///
/// While unlocked the hook only watches for a double-tap of Caps Lock, which
/// is an alternative way to summon the curtain.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;

    private const int VkTab = 0x09;
    private const int VkEscape = 0x1B;
    private const int VkCapital = 0x14;
    private const int VkMenu = 0x12;    // Alt
    private const int VkControl = 0x11; // Ctrl
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int VkApps = 0x5D;
    private const int VkF4 = 0x73;
    private const int VkSnapshot = 0x2C;

    private const int DoubleCapsWindowMs = 400;

    private readonly Func<bool> _isLocked;
    private readonly Action _onDoubleCaps;
    private readonly HookProc _proc;
    private IntPtr _hook;
    private long _lastCapsDown;
    private bool _capsHeld;

    public KeyboardHook(Func<bool> isLocked, Action onDoubleCaps)
    {
        _isLocked = isLocked;
        _onDoubleCaps = onDoubleCaps;
        _proc = HookCallback;

        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            Log.Write("keyboard hook failed: " + Marshal.GetLastWin32Error());
        }
        else
        {
            Log.Write("keyboard hook installed");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var vk = (int)info.vkCode;

            if (message == WmKeyDown || message == WmSysKeyDown)
            {
                if (vk == VkCapital && !_capsHeld)
                {
                    _capsHeld = true;
                    var now = Environment.TickCount64;

                    if (now - _lastCapsDown <= DoubleCapsWindowMs)
                    {
                        _lastCapsDown = 0;
                        _onDoubleCaps();
                    }
                    else
                    {
                        _lastCapsDown = now;
                    }
                }

                if (_isLocked() && IsBlocked(vk))
                {
                    return (IntPtr)1;
                }
            }
            else if ((message == WmKeyUp || message == WmSysKeyUp) && vk == VkCapital)
            {
                _capsHeld = false;
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool IsBlocked(int vk)
    {
        if (vk is VkLWin or VkRWin or VkApps or VkSnapshot)
        {
            return true;
        }

        if (vk == VkTab && IsDown(VkMenu))
        {
            return true; // Alt+Tab
        }

        if (vk == VkEscape && IsDown(VkControl))
        {
            return true; // Ctrl+Esc
        }

        if (vk == VkF4 && IsDown(VkMenu))
        {
            return true; // Alt+F4
        }

        return false;
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            Log.Write("keyboard hook removed");
        }
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
