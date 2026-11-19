using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TermLock.Core;

/// <summary>
/// USB key: a random token in termlock.key on a removable drive, bound to the
/// volume serial. The config stores only the SHA-256 of the token and the serial.
/// </summary>
public static class UsbKey
{
    public const string FileName = "termlock.key";
    private const string Header = "TERMLOCK1";

    public static IReadOnlyList<DriveInfo> RemovableDrives()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(drive =>
                {
                    try
                    {
                        return drive.DriveType == DriveType.Removable && drive.IsReady;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToArray();
        }
        catch
        {
            return Array.Empty<DriveInfo>();
        }
    }

    public static (string Hash, uint VolumeSerial) Create(string root)
    {
        var token = RandomNumberGenerator.GetBytes(32);
        var payload = $"{Header}\n{Convert.ToBase64String(token)}\n";

        File.WriteAllText(
            Path.Combine(root, FileName),
            payload,
            Encoding.ASCII);

        return (HashToken(token), GetVolumeSerial(root));
    }

    public static bool IsPresent(AppConfig config)
    {
        if (!config.HasUsbKey)
        {
            return false;
        }

        foreach (var drive in RemovableDrives())
        {
            try
            {
                var path = Path.Combine(drive.RootDirectory.FullName, FileName);
                if (!File.Exists(path))
                {
                    continue;
                }

                var lines = File.ReadAllLines(path);
                if (lines.Length < 2 || lines[0].Trim() != Header)
                {
                    continue;
                }

                var token = Convert.FromBase64String(lines[1].Trim());
                if (GetVolumeSerial(drive.RootDirectory.FullName) != config.UsbVolumeSerial)
                {
                    continue;
                }

                var actual = Convert.FromBase64String(HashToken(token));
                var expected = Convert.FromBase64String(config.UsbTokenHash);

                if (CryptographicOperations.FixedTimeEquals(actual, expected))
                {
                    return true;
                }
            }
            catch
            {
                // keep looking at the other drives
            }
        }

        return false;
    }

    private static string HashToken(byte[] token) =>
        Convert.ToBase64String(SHA256.HashData(token));

    private static uint GetVolumeSerial(string root)
    {
        try
        {
            return GetVolumeInformation(
                root,
                null,
                0,
                out var serial,
                out _,
                out _,
                null,
                0)
                ? serial
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetVolumeInformation(
        string rootPathName,
        StringBuilder? volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder? fileSystemNameBuffer,
        int fileSystemNameSize);
}
