using System.Diagnostics;

namespace MacroEngine.Core;

internal static class AppLog
{
    private static readonly object Sync = new();
    private const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Detailed keyboard diagnostics are opt-in only. Enable them by creating
    /// %LocalAppData%\MacroEngine\state\diagnostic.logging (or the equivalent
    /// portable state path), or by setting MACROENGINE_DIAGNOSTIC=1.
    /// </summary>
    public static bool DiagnosticEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("MACROENGINE_DIAGNOSTIC"), "1", StringComparison.Ordinal)
        || File.Exists(AppPaths.DiagnosticMarker);

    public static void Write(string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
        Debug.WriteLine(line);

        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LogDirectory);
                RotateIfNeeded();
                File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never bring down the automation engine.
            }
        }
    }

    public static void Diagnostic(string message)
    {
        if (DiagnosticEnabled)
            Write("[DIAGNOSTIC] " + message);
    }

    private static void RotateIfNeeded()
    {
        var info = new FileInfo(AppPaths.LogFile);
        if (!info.Exists || info.Length < MaxBytes) return;

        string backup = AppPaths.LogFile + ".1";
        if (File.Exists(backup)) File.Delete(backup);
        File.Move(AppPaths.LogFile, backup);
    }
}
