using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MacroEngine.Core;

/// <summary>
/// Low-level global keyboard hook. Intercepts all key presses system-wide.
/// Fires <see cref="KeyPressed"/> event on every WM_KEYDOWN.
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
        // Keep delegate alive — GC would collect it otherwise
        _hookProc = HookCallback;
    }

    /// <summary>Install the global hook.</summary>
    public void Start()
    {
        if (_hookId != IntPtr.Zero)
            return; // already hooked

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        if (module?.BaseAddress == IntPtr.Zero)
            throw new InvalidOperationException("Cannot get module handle for hook.");

        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _hookProc,
            IntPtr.Zero,   // low-level hooks don't need module handle in .NET
            0);            // 0 = global (all threads)

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
                var kb = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                int vkCode = (int)kb.vkCode;
                uint scanCode = kb.scanCode;

                bool ctrl = (NativeMethods.GetAsyncKeyState(0x11) & 0x8000) != 0;
                bool alt = (NativeMethods.GetAsyncKeyState(0x12) & 0x8000) != 0;
                bool shift = (NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0;

                var args = new KeyEventData(vkCode, scanCode, ctrl, alt, shift);

                // ── Hotkey recording mode ────────────────────────────
                if (IsRecordingHotkey)
                {
                    bool hasCtrl = ctrl || (NativeMethods.GetAsyncKeyState(0xA2) & 0x8000) != 0;
                    bool hasAlt  = alt  || (NativeMethods.GetAsyncKeyState(0xA4) & 0x8000) != 0;
                    bool hasShift = shift;

                    var parts = new List<string>();
                    if (hasCtrl) parts.Add("Ctrl");
                    if (hasAlt) parts.Add("Alt");
                    if (hasShift) parts.Add("Shift");

                    string keyName = VkToName(vkCode);
                    bool isModifier = keyName.Length == 0;

                    // Cancel on Escape or click-away
                    if (vkCode == 0x1B) // Escape
                    {
                        IsRecordingHotkey = false;
                        HotkeyRecorded?.Invoke("");
                        return (IntPtr)1;
                    }

                    if (!isModifier && parts.Count > 0)
                    {
                        // Final combo
                        parts.Add(keyName);
                        string combo = string.Join("+", parts);
                        if (!SystemHotkeys.IsSystem(combo))
                        {
                            IsRecordingHotkey = false;
                            LogToFile($"[RECORD] captured: {combo}");
                            HotkeyRecorded?.Invoke(combo);
                            return (IntPtr)1;
                        }
                        // System hotkey — keep recording, swallow key
                        return (IntPtr)1;
                    }
                    else if (parts.Count > 0)
                    {
                        // Modifiers held — show live preview
                        string partial = string.Join("+", parts) + "+…";
                        HotkeyRecording?.Invoke(partial);
                        return (IntPtr)1;
                    }
                    else
                    {
                        // No modifiers yet — swallow key, wait for Ctrl/Alt
                        HotkeyRecording?.Invoke("Нажмите Ctrl/Alt...");
                        return (IntPtr)1;
                    }
                }

                KeyPressed?.Invoke(args);

                // Suppress keystroke if hotkey handler requested it
                if (SuppressKey)
                {
                    SuppressKey = false;
                    return (IntPtr)1; // Block keystroke from reaching target app
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
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
            0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
            _ => ""
        };
    }

    private static void LogToFile(string message)
    {
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "macroengine.log");
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch { }
    }
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

    /// <summary>Cached character — computed once via ToUnicode.</summary>
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
    /// Convert virtual key + scan code to a Unicode character
    /// using the current keyboard layout (via ToUnicode).
    /// Returns '\0' if the key does not produce a character.
    /// </summary>
    public char ToChar()
    {
        if (_cachedChar.HasValue)
            return _cachedChar.Value;

        _cachedChar = '\0';

        // Modifier keys alone don't produce characters
        if (VirtualKeyCode is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C) // Shift/Ctrl/Alt/Win
            return '\0';

        // Let ToUnicode decide whether the key produces a character.
        // (Previously we returned '\0' for any Ctrl/Alt, which blocked
        //  AltGr combinations like AltGr+2 → @ on Russian layout.)

        try
        {
            // Get current keyboard state (256 bytes)
            byte[] keyState = new byte[256];
            if (!NativeMethods.GetKeyboardState(keyState))
                return '\0';

            // Force Shift state in the keyboard state buffer
            if (Shift)
                keyState[0x10] = 0x80; // VK_SHIFT pressed

            // Call ToUnicodeEx with the foreground window's keyboard layout,
            // not the hook thread's layout (they can differ!).
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

            // result == 1 means one character was produced
            // result > 1 means dead key (ignore for now)
            if (result == 1 && sb.Length > 0)
            {
                _cachedChar = sb[0];
            }
        }
        catch
        {
            // Fallback: ignore errors in character conversion
        }

        return _cachedChar.Value;
    }
}
