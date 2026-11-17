using System.IO;

namespace TermLock.Core;

/// <summary>
/// Dead-simple append-only log. No dependencies, no telemetry, no network.
/// If logging fails we swallow the error: a broken log must never break the lock.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            AppPaths.Ensure();
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(AppPaths.LogFile, line);
            }
        }
        catch
        {
            // intentionally ignored
        }
    }
}
