using Microsoft.Win32;

namespace TermLock.Core;

/// <summary>
/// Friendly Windows version label, e.g. "Windows 10 22H2" or "Windows 11 24H2".
/// Read from HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion — no WMI, no network.
/// </summary>
public static class SystemInfo
{
    public static string WindowsLabel()
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

            foreach (var edition in new[]
                     {
                         " Pro", " Home", " Enterprise", " Education", " IoT", " N"
                     })
            {
                product = product.Replace(edition, "");
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
