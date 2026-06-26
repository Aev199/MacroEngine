using Microsoft.Win32;

namespace MacroEngine.Core;

/// <summary>
/// Registers / unregisters MacroEngine to launch on user login via the
/// per-user HKCU ...\Run key (no admin rights required).
/// </summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MacroEngine";

    private static string ExePath() => Environment.ProcessPath ?? "";

    /// <summary>True if the Run entry points at the current executable.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s
                && s.Trim('"').Equals(ExePath(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (key == null) return;

        if (enabled)
            key.SetValue(ValueName, $"\"{ExePath()}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
