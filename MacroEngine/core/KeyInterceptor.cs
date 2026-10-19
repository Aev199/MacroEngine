using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MacroEngine.Core;

/// <summary>
/// Low-level global keyboard hook. Intercepts all key presses system-wide.
/// Fires <see cref="KeyPressed"/> on every WM_KEYDOWN.
/// </summary>
internal sealed class KeyInterceptor : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _hookProc;
    private IntPtr _hookId = IntPtr.Zero;
    private bool _disposed;

    /// <summary>
    /// Set to true while TextExpander is sending keystrokes —
    /// suppresses trigger detection to avoid feedback loops.
    /// </summary>
    public static volatile bool IsSuppressed;

    /// <summary>
    /// Set to true to block the current keystroke from reaching the target app.
    /// Reset by the hook callback after processing. Use for hotkey triggers.
    /// </summary>
    public static volatile bool SuppressKey;

    /// <summary>When true, next key combo is captured as a hotkey string.</summary>
    public static volatile bool IsRecordingHotkey;

    /// <summary>Receives the captured hotkey combo (e.g. "Ctrl+Shift+K").</summary>
    public static event Action<string>? HotkeyRecorded;

    /// <summary>Live feedback during recording — partial combo like "Ctrl+Shift".</summary>
    public static event Action<string>? HotkeyRecording;

    /// <summary>Fired on key down (WM_KEYDOWN / WM_SYSKEYDOWN).</summary>
    public event Action<KeyEventData>? KeyPressed;

    public KeyInterceptor()
    {
        // Keep delegate alive — GC would collect it otherwise.
        _hookProc = HookCallback;
    }

    /// <summary>Install the global hook.</summary>
    public void Start()
    {
        if (_hookId != IntPtr.Zero)
            return;

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        if (module?.BaseAddress == IntPtr.Zero)
            throw new InvalidOperationException("Cannot get module handle for hook.");

        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _hookProc,
            IntPtr.Zero,
            0);

        if (_hookId == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SetWindowsHookEx failed with error {err}");
        }
    }

    /// <summary>Uninstall the global hook.</summary>
    public void Stop()
    {
        if (_hookId == IntPtr.Zero)
            return;

        NativeMethods.UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !IsSuppressed)
        {
            int msg = wParam.ToInt32();
            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                // Never process or swallow input while one of MacroEngine's own
                // windows is active. This keeps settings, prompts and the hotkey
                // recorder isolated from the global trigger engine.
                if (IsOwnWindowForeground())
                {
                    SuppressKey = false;
                    return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                var kb = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                int vkCode = (int)kb.vkCode;
                uint scanCode = kb.scanCode;

                bool ctrl = (NativeMethods.GetAsyncKeyState(0x11) & 0x8000) != 0;
                bool alt = (NativeMethods.GetAsyncKeyState(0x12) & 0x8000) != 0;
                bool shift = (NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0;

                var args = new KeyEventData(vkCode, scanCode, ctrl, alt, shift);

                // Legacy hook-recording mode is retained for compatibility.
                if (IsRecordingHotkey)
                {
                    bool hasCtrl = ctrl || (NativeMethods.GetAsyncKeyState(0xA2) & 0x8000) != 0;
                    bool hasAlt = alt || (NativeMethods.GetAsyncKeyState(0xA4) & 0x8000) != 0;
                    bool hasShift = shift;

                    var parts = new List<string>();
                    if (hasCtrl) parts.Add("Ctrl");
                    if (hasAlt) parts.Add("Alt");
                    if (hasShift) parts.Add("Shift");

                    string keyName = VkToName(vkCode);
                    bool isModifier = keyName.Length == 0;

                    if (vkCode == 0x1B)
                    {
                        IsRecordingHotkey = false;
                        HotkeyRecorded?.Invoke("");
                        return (IntPtr)1;
                    }

                    if (!isModifier && parts.Count > 0)
                    {
                        parts.Add(keyName);
                        string combo = string.Join("+", parts);
                        if (!SystemHotkeys.IsSystem(combo))
                        {
                            IsRecordingHotkey = false;
                            LogToFile($"[RECORD] captured: {combo}");
                            HotkeyRecorded?.Invoke(combo);
                            return (IntPtr)1;
                        }

                        return (IntPtr)1;
                    }

                    if (parts.Count > 0)
                    {
                        HotkeyRecording?.Invoke(string.Join("+", parts) + "+…");
                        return (IntPtr)1;
                    }

                    HotkeyRecording?.Invoke("Нажмите Ctrl/Alt...");
                    return (IntPtr)1;
                }

                try
                {
                    KeyPressed?.Invoke(args);
                }
                catch (Exception ex)
                {
                    SuppressKey = false;
                    AppLog.Write($"Keyboard hook handler failed: {ex.GetType().Name}: {ex.Message}");
                }

                if (SuppressKey)
                {
                    SuppressKey = false;
                    return (IntPtr)1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsOwnWindowForeground()
    {
        IntPtr hWnd = NativeMethods.GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return false;

        NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
        return processId == (uint)Environment.ProcessId;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    /// <summary>Convert VK code to readable name for hotkey display.</summary>
    internal static string VkToName(int vk)
    {
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
        if (vk >= 0x70 && vk <= 0x7B) return $"F{vk - 0x6F}";
        if (vk >= 0x60 && vk <= 0x69) return $"Num{vk - 0x60}";
        return vk switch
        {
            0x20 => "Space", 0x0D => "Enter", 0x1B => "Escape", 0x09 => "Tab",
            0x08 => "Back", 0x2E => "Delete", 0x2D => "Insert",
            0x21 => "PageUp", 0x22 => "PageDown", 0x23 => "End", 0x24 => "Home",
            0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
            _ => ""
        };
    }

    private static void LogToFile(string message) => AppLog.Write(message);
}

/// <summary>
/// Simple key event data (avoiding name clash with WinForms KeyEventArgs).
/// Uses ToUnicode() for accurate character mapping across keyboard layouts.
/// </summary>
internal sealed class KeyEventData
{
    public int VirtualKeyCode { get; }
    public uint ScanCode { get; }
    public bool Control { get; }
    public bool Alt { get; }
    public bool Shift { get; }

    private char? _cachedChar;

    public KeyEventData(int vkCode, uint scanCode, bool ctrl, bool alt, bool shift)
    {
        VirtualKeyCode = vkCode;
        ScanCode = scanCode;
        Control = ctrl;
        Alt = alt;
        Shift = shift;
    }

    /// <summary>
    /// Convert virtual key + scan code to a Unicode character using the
    /// foreground window's keyboard layout. Returns '\0' for non-text keys.
    /// </summary>
    public char ToChar()
    {
        if (_cachedChar.HasValue)
            return _cachedChar.Value;

        _cachedChar = '\0';

        if (VirtualKeyCode is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C)
            return '\0';

        try
        {
            byte[] keyState = new byte[256];
            if (!NativeMethods.GetKeyboardState(keyState))
                return '\0';

            if (Shift)
                keyState[0x10] = 0x80;

            var sb = new System.Text.StringBuilder(4);
            IntPtr hkl = WindowContext.GetForegroundKeyboardLayout();
            int result = NativeMethods.ToUnicodeEx(
                (uint)VirtualKeyCode,
                ScanCode,
                keyState,
                sb,
                sb.Capacity,
                0,
                hkl);

            if (result == 1 && sb.Length > 0)
                _cachedChar = sb[0];
        }
        catch
        {
            // Character conversion is best-effort; non-text keys are ignored.
        }

        return _cachedChar.Value;
    }
}
