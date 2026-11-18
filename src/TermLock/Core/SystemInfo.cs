using Microsoft.Win32;

namespace TermLock.Core;

/// <summary>
/// Friendly Windows version label, e.g. "Windows 10 Pro 22H2" or
/// "Windows 11 Pro 24H2". Read from
/// HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion — no WMI, no network.
/// The label is cached: the OS version does not change while the app runs.
/// </summary>
public static class SystemInfo
{
    private static readonly Lazy<string> CachedLabel = new(DetectWindowsLabel);

    public static string WindowsLabel() => CachedLabel.Value;

    private static string DetectWindowsLabel()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

            var product = key?.GetValue("ProductName") as string ?? "Windows";
            var version = key?.GetValue("DisplayVersion") as string;
            var buildText = key?.GetValue("CurrentBuildNumber") as string;

            // Windows 11 still reports "Windows 10 ..." in ProductName on some
            // builds; the build number is the reliable switch.
            if (int.TryParse(buildText, out var build) && build >= 22000)
            {
                product = product.Replace("Windows 10", "Windows 11");
            }

            product = product.Trim();

            return string.IsNullOrWhiteSpace(version)
                ? product
                : $"{product} {version}";
        }
        catch
        {
            return "Windows";
        }
    }
}
