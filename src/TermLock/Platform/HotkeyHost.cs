using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace TermLock.Platform;

/// <summary>
/// Owns a hidden Win32 message window and a system-wide hotkey.
///
/// How it works:
///   1. HwndSource creates an invisible HWND on the UI thread.
///   2. RegisterHotKey tells Windows: "when this combo is pressed, post WM_HOTKEY
///      to that HWND", no matter which app has focus.
///   3. WndProc sees WM_HOTKEY and raises <see cref="Pressed"/> on the UI thread.
///
/// MOD_NOREPEAT stops auto-repeat when the user keeps the keys held down.
/// </summary>
public sealed class HotkeyHost : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkL = 0x4C;

    private readonly HwndSource _source;
    private readonly int _id;
    private bool _registered;

    public event EventHandler? Pressed;

    public HotkeyHost(int id = 0x544C)
    {
        _id = id;

        var parameters = new HwndSourceParameters("TermLock.HotkeySink")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _registered = RegisterHotKey(
            _source.Handle,
            _id,
            ModControl | ModAlt | ModShift | ModNoRepeat,
            VkL);
    }

    public bool IsRegistered => _registered;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == _id)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered)
        {
            UnregisterHotKey(_source.Handle, _id);
            _registered = false;
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
