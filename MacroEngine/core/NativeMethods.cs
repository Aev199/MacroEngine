using System.Runtime.InteropServices;

namespace MacroEngine.Core;

/// <summary>
/// P/Invoke declarations for Windows API functions used by MacroEngine.
/// </summary>
internal static class NativeMethods
{
    // ── Keyboard Hook ──────────────────────────────────────────────

    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr CallNextHookEx(
        IntPtr hhk,
        int nCode,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string lpModuleName);

    // ── Keyboard State ─────────────────────────────────────────────

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetKeyboardState(byte[] lpKeyState);

    [DllImport("user32.dll")]
    public static extern int ToUnicodeEx(
        uint wVirtKey,
        uint wScanCode,
        byte[] lpKeyState,
        [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszBuff,
        int cchBuff,
        uint wFlags,
        IntPtr hkl);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    public static extern IntPtr GetKeyboardLayout(uint idThread);

    public const uint MAPVK_VK_TO_VSC = 0;

    // ── Foreground Window ──────────────────────────────────────────

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    /// <summary>Get the window class name (e.g. "Notepad", "Chrome_WidgetWin_1", "AcadFrame").</summary>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    // ── Input Simulation ───────────────────────────────────────────

    public const int INPUT_KEYBOARD = 1;
    public const int INPUT_MOUSE = 0;
    public const uint KEYEVENTF_KEYDOWN = 0x0000;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    /// <summary>Keyboard input structure — explicit layout for correct x64 sizing.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct KEYBDINPUT
    {
        [FieldOffset(0)] public ushort wVk;
        [FieldOffset(2)] public ushort wScan;
        [FieldOffset(4)] public uint dwFlags;
        [FieldOffset(8)] public uint time;
        [FieldOffset(16)] public IntPtr dwExtraInfo; // 8-byte aligned on x64
    }

    /// <summary>
    /// INPUT structure — must be 40 bytes on x64 (28 on x86).
    /// Union starts at offset 8 on x64 due to 8-byte alignment of largest member.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct INPUT
    {
        [FieldOffset(0)] public uint type;
        // Keyboard (union member)
        [FieldOffset(8)] public KEYBDINPUT ki;
        // Dummy Mouse to force correct union size (MOUSEINPUT = 32 bytes on x64)
        [FieldOffset(8)] public MOUSEINPUT mi;
    }

    /// <summary>Minimal MOUSEINPUT just to force correct struct size.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct MOUSEINPUT
    {
        [FieldOffset(0)] public int dx;
        [FieldOffset(4)] public int dy;
        [FieldOffset(8)] public uint mouseData;
        [FieldOffset(12)] public uint dwFlags;
        [FieldOffset(16)] public uint time;
        [FieldOffset(24)] public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, [MarshalAs(UnmanagedType.LPArray), In] INPUT[] pInputs, int cbSize);

    // ── Clipboard ──────────────────────────────────────────────────

    /// <summary>Returns a monotonically increasing number that changes on every clipboard update.</summary>
    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    // ── Clipboard (WinAPI — more reliable than .NET Clipboard on background threads) ─

    public const uint CF_UNICODETEXT = 13;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseClipboard();

    [DllImport("kernel32.dll")]
    public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalUnlock(IntPtr hMem);

    /// <summary>Register a custom clipboard format, or get existing ID.</summary>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern uint RegisterClipboardFormat(string lpszFormat);

    // ── Foreground Event Hook ──────────────────────────────────────

    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;

    public delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    // ── KBDLLHOOKSTRUCT ────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
