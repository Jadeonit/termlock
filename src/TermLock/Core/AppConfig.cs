using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TermLock.Core;

/// <summary>
/// User-facing settings. Stored as JSON in %APPDATA%\TermLock\config.json.
/// Secrets are never stored in plaintext: only a PBKDF2 hash and its salt.
/// </summary>
public sealed class AppConfig
{
    /// <summary>Shown in the terminal header. Not the Windows hostname.</summary>
    public string DisplayName { get; set; } = "Jadeonit-laptop";

    /// <summary>Name typed during setup; the curtain renders "{UserName} @ {DeviceType} @ {WindowsLabel}".</summary>
    public string UserName { get; set; } = "Jadeonit";

    /// <summary>"PC" or "Laptop".</summary>
    public string DeviceType { get; set; } = "PC";

    /// <summary>Convenience for the UI: "Jadeonit @ Laptop @ Windows 10 Pro 22H2".</summary>
    [JsonIgnore]
    public string DisplayNameFull
    {
        get
        {
            if (string.IsNullOrWhiteSpace(UserName))
            {
                return DisplayName;
            }

            var device = string.Equals(DeviceType, "Laptop", StringComparison.OrdinalIgnoreCase)
                ? "Laptop"
                : "PC";

            return $"{UserName} @ {device} @ {SystemInfo.WindowsLabel()}";
        }
    }

    public string PassphraseHash { get; set; } = "";
    public string PassphraseSalt { get; set; } = "";
    public int PassphraseIterations { get; set; } = 600_000;

    public string RecoveryHash { get; set; } = "";
    public string UsbTokenHash { get; set; } = "";
    public uint UsbVolumeSerial { get; set; }

    /// <summary>Human-readable form; the parser lives in HotkeyHost (Day 5).</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+Shift+L";

    public int MaxAttempts { get; set; } = 3;
    public int CooldownSeconds { get; set; } = 10;

    /// <summary>Shutdown / Sleep / Hibernate / Lock / None. Wired up on Day 8.</summary>
    public string FailAction { get; set; } = "Shutdown";

    public bool LockOnStart { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    [JsonIgnore]
    public bool HasPassphrase =>
        !string.IsNullOrWhiteSpace(PassphraseHash) && !string.IsNullOrWhiteSpace(PassphraseSalt);

    [JsonIgnore]
    public bool HasRecovery => !string.IsNullOrWhiteSpace(RecoveryHash);

    [JsonIgnore]
    public bool HasUsbKey => !string.IsNullOrWhiteSpace(UsbTokenHash);

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(AppPaths.ConfigFile))
            {
                var json = File.ReadAllText(AppPaths.ConfigFile);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (config is not null)
                {
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("config load failed: " + ex.Message);
        }

        return new AppConfig();
    }

    public void Save()
    {
        AppPaths.Ensure();
        File.WriteAllText(AppPaths.ConfigFile, JsonSerializer.Serialize(this, JsonOptions));
    }
}
