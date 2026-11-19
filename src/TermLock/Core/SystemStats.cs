using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TermLock.Core;

/// <summary>
/// Lightweight system probes for the curtain dashboard.
/// CPU: GetSystemTimes deltas. RAM: GlobalMemoryStatusEx. Disk: DriveInfo.
/// Defender/firewall/secure boot: registry + process checks.
/// No WMI, no network, no admin rights.
/// </summary>
public sealed class SystemStats
{
    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private bool _hasCpuSample;

    public double CpuPercent { get; private set; }
    public double RamPercent { get; private set; }
    public double RamUsedGb { get; private set; }
    public double RamTotalGb { get; private set; }
    public double DiskPercent { get; private set; }
    public double DiskFreeGb { get; private set; }
    public double DiskTotalGb { get; private set; }
    public string DiskName { get; private set; } = "C:";
    public bool DefenderActive { get; private set; }
    public bool FirewallActive { get; private set; }
    public bool? SecureBoot { get; private set; }

    public void Refresh()
    {
        RefreshCpu();
        RefreshMemory();
        RefreshDisk();
        RefreshSecurity();
    }

    public void ResetCpuBaseline()
    {
        CpuPercent = 0;
        _hasCpuSample = false;
        Refresh();
    }

    public static string Bar(double percent, int width = 10)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        var filled = Math.Clamp((int)Math.Round(clamped / 100.0 * width), 0, width);
        return new string('█', filled) + new string('░', width - filled);
    }

    private void RefreshCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return;
        }

        var idleTicks = ToTicks(idle);
        var kernelTicks = ToTicks(kernel);
        var userTicks = ToTicks(user);

        if (_hasCpuSample)
        {
            var idleDelta = idleTicks - _prevIdle;
            var totalDelta = (kernelTicks - _prevKernel) + (userTicks - _prevUser);

            if (totalDelta > 0)
            {
                CpuPercent = Math.Clamp(
                    (1.0 - idleDelta / (double)totalDelta) * 100.0,
                    0,
                    100);
            }
        }

        _prevIdle = idleTicks;
        _prevKernel = kernelTicks;
        _prevUser = userTicks;
        _hasCpuSample = true;
    }

    private void RefreshMemory()
    {
        var status = new MemoryStatusEx
        {
            dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };

        if (!GlobalMemoryStatusEx(ref status))
        {
            return;
        }

        RamPercent = status.dwMemoryLoad;
        RamTotalGb = status.ullTotalPhys / 1073741824.0;
        RamUsedGb = (status.ullTotalPhys - status.ullAvailPhys) / 1073741824.0;
    }

    private void RefreshDisk()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);

            if (!drive.IsReady)
            {
                return;
            }

            var total = drive.TotalSize;
            var free = drive.AvailableFreeSpace;

            DiskName = drive.Name.TrimEnd('\\');
            DiskTotalGb = total / 1073741824.0;
            DiskFreeGb = free / 1073741824.0;
            DiskPercent = total > 0
                ? Math.Clamp((1.0 - free / (double)total) * 100.0, 0, 100)
                : 0;
        }
        catch
        {
            // keep the last values
        }
    }

    private void RefreshSecurity()
    {
        try
        {
            var defenderRunning = Process.GetProcessesByName("MsMpEng").Length > 0;
            var realtimeDisabled =
                ReadDword(
                    @"SOFTWARE\Microsoft\Windows Defender\Real-Time Protection",
                    "DisableRealtimeMonitoring") == 1;
            var policyDisabled =
                ReadDword(
                    @"SOFTWARE\Policies\Microsoft\Windows Defender",
                    "DisableAntiSpyware") == 1;

            DefenderActive = defenderRunning && !realtimeDisabled && !policyDisabled;
        }
        catch
        {
            DefenderActive = false;
        }

        try
        {
            var profiles = new[]
            {
                "DomainProfile", "StandardProfile", "PublicProfile"
            };

            FirewallActive = profiles.Any(profile =>
                ReadDword(
                    $@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{profile}",
                    "EnableFirewall") == 1);
        }
        catch
        {
            FirewallActive = false;
        }

        try
        {
            var value = ReadDword(
                @"SYSTEM\CurrentControlSet\Control\SecureBoot\State",
                "UEFISecureBootEnabled");

            SecureBoot = value switch
            {
                1 => true,
                0 => false,
                _ => null
            };
        }
        catch
        {
            SecureBoot = null;
        }
    }

    private static int? ReadDword(string subKey, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey);
            var value = key?.GetValue(name);
            return value is int number ? number : null;
        }
        catch
        {
            return null;
        }
    }

    private static ulong ToTicks(FileTime time) =>
        ((ulong)time.dwHighDateTime << 32) | time.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out FileTime idleTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}
