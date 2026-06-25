using System.Diagnostics;
using System.Text;

namespace MacroEngine.Core;

/// <summary>
/// Provides information about the currently active (foreground) window:
/// process name, window class, and title. Used for context matching in triggers.
/// </summary>
internal static class WindowContext
{
    /// <summary>Get the process name of the foreground window.</summary>
    public static string GetActiveProcessName()
    {
        IntPtr hWnd = NativeMethods.GetForegroundWindow();
        if (hWnd == IntPtr.Zero)
            return string.Empty;

        NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
        if (processId == 0)
            return string.Empty;

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Returns a rich context fingerprint for trigger matching:
    ///   "process.exe | ClassName | WindowTitle"
    /// All parts are lowercased for case-insensitive matching.
    /// </summary>
    public static string GetContextFingerprint()
    {
        IntPtr hWnd = NativeMethods.GetForegroundWindow();
        if (hWnd == IntPtr.Zero)
            return string.Empty;

        string proc = GetProcessName(hWnd);
        string cls = GetClassName(hWnd);
        string title = GetTitle(hWnd);

        return $"{proc} | {cls} | {title}".ToLowerInvariant();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Context Matching
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Check whether a context pattern matches the current window.
    /// 
    /// Patterns:
    ///   "*"           — global (always matches)
    ///   "acad"        — substring match anywhere in the fingerprint
    ///   "acad,revit"  — comma-separated: matches if ANY pattern matches (OR logic)
    ///   "!browser"    — prefix "!" means: do NOT match (exclusion)
    /// </summary>
    public static bool MatchesContext(string contextPattern, string? windowFingerprint = null)
    {
        // Global trigger — always matches
        if (contextPattern == "*")
            return true;

        string fingerprint = windowFingerprint ?? GetContextFingerprint();
        if (string.IsNullOrEmpty(fingerprint))
            return false;

        // Split by comma, trim whitespace
        var patterns = contextPattern.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrEmpty(pattern))
                continue;

            // Exclusion pattern: "!browser" — if matched, the trigger is disabled
            if (pattern.StartsWith('!'))
            {
                string exclude = pattern[1..].Trim();
                if (!string.IsNullOrEmpty(exclude) && fingerprint.Contains(exclude, StringComparison.OrdinalIgnoreCase))
                    return false; // Excluded → no match
                continue;
            }

            // Normal pattern: substring match in any part of the fingerprint
            if (fingerprint.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true; // First match wins (OR logic)
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Keyboard Layout
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns a short description of the current keyboard layout (e.g. "RU", "EN-US").
    /// </summary>
    public static string GetKeyboardLayoutTag()
    {
        try
        {
            IntPtr hkl = GetForegroundKeyboardLayout();
            if (hkl == IntPtr.Zero)
                return "??";

            uint langId = (uint)hkl.ToInt64() & 0xFFFF;

            return langId switch
            {
                0x0409 => "EN-US",
                0x0809 => "EN-UK",
                0x0419 => "RU",
                0x0407 => "DE",
                0x040C => "FR",
                _ => $"0x{langId:X4}"
            };
        }
        catch
        {
            return "??";
        }
    }

    /// <summary>Get the HKL (keyboard layout handle) for the foreground window's thread.</summary>
    public static IntPtr GetForegroundKeyboardLayout()
    {
        IntPtr hWnd = NativeMethods.GetForegroundWindow();
        if (hWnd == IntPtr.Zero)
            return IntPtr.Zero;

        uint threadId = NativeMethods.GetWindowThreadProcessId(hWnd, out _);
        return NativeMethods.GetKeyboardLayout(threadId);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Private helpers (single hWnd → avoid multiple GetForegroundWindow calls)
    // ═══════════════════════════════════════════════════════════════

    private static string GetTitle(IntPtr hWnd)
    {
        int length = NativeMethods.GetWindowTextLength(hWnd);
        if (length == 0) return string.Empty;
        var sb = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        NativeMethods.GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetProcessName(IntPtr hWnd)
    {
        NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
        if (processId == 0) return string.Empty;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }
}
