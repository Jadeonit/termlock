using Microsoft.Win32;

namespace TermLock.Core;

/// <summary>
/// Friendly Windows labels:
///   WindowsProduct() -> "Windows 10 Pro"
///   WindowsLabel()   -> "Windows 10 Pro 22H2"
///
/// Read from HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion — no WMI, no network.
/// The pair is cached: the OS version does not change while the app runs.
/// </summary>
public static class SystemInfo
{
    private static readonly Lazy<(string Product, string Version)> Cached = new(Detect);

    /// <summary>Product name only, e.g. "Windows 10 Pro".</summary>
    public static string WindowsProduct() => Cached.Value.Product;

    /// <summary>Product + feature update, e.g. "Windows 10 Pro 22H2".</summary>
    public static string WindowsLabel()
    {
        var (product, version) = Cached.Value;
        return string.IsNullOrWhiteSpace(version) ? product : $"{product} {version}";
    }

    private static (string Product, string Version) Detect()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

            var product = key?.GetValue("ProductName") as string ?? "Windows";
            var version = key?.GetValue("DisplayVersion") as string ?? "";
            var buildText = key?.GetValue("CurrentBuildNumber") as string;

            // Windows 11 still reports "Windows 10 ..." in ProductName on some
            // builds; the build number is the reliable switch.
            if (int.TryParse(buildText, out var build) && build >= 22000)
            {
                product = product.Replace("Windows 10", "Windows 11");
            }

            return (product.Trim(), version);
        }
        catch
        {
            return ("Windows", "");
        }
    }
}
