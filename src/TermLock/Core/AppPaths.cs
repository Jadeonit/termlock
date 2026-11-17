using System.IO;

namespace TermLock.Core;

/// <summary>
/// Every mutable file TermLock owns lives under %APPDATA%\TermLock.
/// Nothing is written next to the exe, so the app works from any folder
/// and a non-admin user never needs write access to Program Files.
/// </summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TermLock");

    public static string ConfigFile => Path.Combine(Root, "config.json");

    public static string LogFile => Path.Combine(Root, "logs", "termlock.log");

    public static void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
    }
}
