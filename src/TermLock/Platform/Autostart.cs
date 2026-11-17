using System.Diagnostics;
using Microsoft.Win32;
using TermLock.Core;

namespace TermLock.Platform;

/// <summary>
/// Autostart via HKCU, not a scheduled task and not a service.
///
/// HKCU needs no administrator rights and is easy for the owner to remove
/// (Task Manager -> Startup, or `TermLock.exe --uninstall`).
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TermLock";

    private static string ExePath =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? string.Empty;

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string value &&
                   value.Contains("TermLock", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key?.SetValue(ValueName, $"\"{ExePath}\" --autostart");
        Log.Write("autostart enabled");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
        Log.Write("autostart disabled");
    }
}
