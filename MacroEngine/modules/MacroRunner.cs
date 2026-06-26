using System.Diagnostics;
using System.Runtime.InteropServices;
using MacroEngine.Core;

namespace MacroEngine.Modules;

/// <summary>
/// Executes a multi-step macro (action="macro"). The value is a newline-separated
/// script; each line is "verb args":
///
///   type  &lt;text&gt;     type text (supports {tokens})
///   key   &lt;combo&gt;    press a key combo — "key Ctrl+S", "key Enter", "key F5"
///   sleep &lt;ms&gt;       wait N milliseconds
///   click &lt;x,y&gt;      left-click at screen coords (omit coords → click at cursor)
///   dclick &lt;x,y&gt;     double left-click
///   rclick &lt;x,y&gt;     right-click
///   run   &lt;command&gt;  launch an external program (fire-and-forget)
///
/// Blank lines and lines starting with '#' are ignored.
/// </summary>
internal static class MacroRunner
{
    private enum MouseButton { Left, Right }

    public static void Run(string script, int eraseLen)
    {
        // Suppress trigger detection so our own synthesized keys don't feed back
        // into the hook (a "key Ctrl+S" must not re-match a configured shortcut).
        KeyInterceptor.IsSuppressed = true;
        IntPtr target = NativeMethods.GetForegroundWindow();
        try
        {
            Thread.Sleep(60);

            // For typed-text triggers the trigger chars are still in the document.
            for (int i = 0; i < eraseLen; i++) { SendVk(0x08); Thread.Sleep(15); }
            if (eraseLen > 0) Thread.Sleep(30);

            foreach (var rawLine in script.Replace("\r", "").Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                // Keep focus on the original window (a {input} prompt may have stolen it).
                if (target != IntPtr.Zero) NativeMethods.SetForegroundWindow(target);

                int sp = line.IndexOf(' ');
                string verb = (sp < 0 ? line : line[..sp]).ToLowerInvariant();
                string arg = sp < 0 ? "" : line[(sp + 1)..].Trim();

                try { Execute(verb, arg); }
                catch (Exception ex) { Log($"[Macro] step failed '{line}': {ex.Message}"); }
            }
        }
        finally { KeyInterceptor.IsSuppressed = false; }
    }

    private static void Execute(string verb, string arg)
    {
        switch (verb)
        {
            case "type":   TextExpander.TypeText(arg); break;
            case "key":    SendCombo(arg); break;
            case "click":  MouseClick(arg, MouseButton.Left, doubleClick: false); break;
            case "dclick": MouseClick(arg, MouseButton.Left, doubleClick: true); break;
            case "rclick": MouseClick(arg, MouseButton.Right, doubleClick: false); break;
            case "run":    Launch(arg); break;
            case "sleep":
                if (int.TryParse(arg, out int ms) && ms > 0)
                    Thread.Sleep(Math.Min(ms, 60_000));
                break;
            default:
                Log($"[Macro] unknown verb: {verb}");
                break;
        }
    }

    // ── Keyboard ────────────────────────────────────────────────────

    private static void SendCombo(string combo)
    {
        var parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return;

        var mods = new List<ushort>();
        ushort main = 0;
        foreach (var p in parts)
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl": case "control": mods.Add(0x11); break;
                case "alt":                  mods.Add(0x12); break;
                case "shift":                mods.Add(0x10); break;
                case "win":                  mods.Add(0x5B); break;
                default:                     main = NameToVk(p); break;
            }
        }
        if (main == 0 && mods.Count == 0) return;

        var inputs = new List<NativeMethods.INPUT>();
        foreach (var m in mods) inputs.Add(KeyInput(m, up: false));
        if (main != 0)
        {
            inputs.Add(KeyInput(main, up: false));
            inputs.Add(KeyInput(main, up: true));
        }
        for (int i = mods.Count - 1; i >= 0; i--) inputs.Add(KeyInput(mods[i], up: true));

        NativeMethods.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void SendVk(ushort vk)
    {
        var inputs = new[] { KeyInput(vk, up: false), KeyInput(vk, up: true) };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT KeyInput(ushort vk, bool up)
    {
        uint scan = NativeMethods.MapVirtualKey(vk, NativeMethods.MAPVK_VK_TO_VSC);
        // Backspace, the navigation cluster (0x21–0x2E) and the Win keys are extended.
        bool extended = vk == 0x08 || (vk >= 0x21 && vk <= 0x2E) || vk == 0x5B || vk == 0x5C;
        uint flags = (extended ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0u)
                   | (up ? NativeMethods.KEYEVENTF_KEYUP : 0u);
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = vk,
                wScan = (ushort)scan,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };
    }

    private static ushort NameToVk(string name)
    {
        if (name.Length == 0) return 0;

        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if (c >= 'A' && c <= 'Z') return c;
            if (c >= '0' && c <= '9') return c;
        }

        if ((name[0] is 'f' or 'F') && int.TryParse(name[1..], out int fn) && fn is >= 1 and <= 24)
            return (ushort)(0x70 + fn - 1);

        return name.ToLowerInvariant() switch
        {
            "enter" or "return" => 0x0D,
            "tab"               => 0x09,
            "space"             => 0x20,
            "esc" or "escape"   => 0x1B,
            "back" or "backspace" => 0x08,
            "delete" or "del"   => 0x2E,
            "insert" or "ins"   => 0x2D,
            "home"              => 0x24,
            "end"               => 0x23,
            "pageup" or "pgup"  => 0x21,
            "pagedown" or "pgdn" => 0x22,
            "left"  => 0x25,
            "up"    => 0x26,
            "right" => 0x27,
            "down"  => 0x28,
            _ => 0
        };
    }

    // ── Mouse ───────────────────────────────────────────────────────

    private static void MouseClick(string arg, MouseButton button, bool doubleClick)
    {
        if (arg.Length > 0)
        {
            var xy = arg.Split(',', StringSplitOptions.TrimEntries);
            if (xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y))
            {
                NativeMethods.SetCursorPos(x, y);
                Thread.Sleep(20);
            }
        }

        SendMouseClick(button);
        if (doubleClick) { Thread.Sleep(40); SendMouseClick(button); }
    }

    private static void SendMouseClick(MouseButton button)
    {
        uint down = button == MouseButton.Left ? NativeMethods.MOUSEEVENTF_LEFTDOWN : NativeMethods.MOUSEEVENTF_RIGHTDOWN;
        uint up   = button == MouseButton.Left ? NativeMethods.MOUSEEVENTF_LEFTUP   : NativeMethods.MOUSEEVENTF_RIGHTUP;
        var inputs = new[] { MouseInput(down), MouseInput(up) };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT MouseInput(uint flags) => new()
    {
        type = NativeMethods.INPUT_MOUSE,
        mi = new NativeMethods.MOUSEINPUT
        {
            dx = 0,
            dy = 0,
            mouseData = 0,
            dwFlags = flags,
            time = 0,
            dwExtraInfo = IntPtr.Zero
        }
    };

    // ── External programs ───────────────────────────────────────────

    private static void Launch(string command)
    {
        try
        {
            string fileName, arguments;
            if (command.StartsWith('"'))
            {
                int end = command.IndexOf('"', 1);
                fileName = end > 1 ? command[1..end] : command;
                arguments = end > 1 ? command[(end + 1)..].Trim() : "";
            }
            else
            {
                int sp = command.IndexOf(' ');
                fileName = sp > 0 ? command[..sp] : command;
                arguments = sp > 0 ? command[(sp + 1)..].Trim() : "";
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true
            });
        }
        catch (Exception ex) { Log($"[Macro] run failed '{command}': {ex.Message}"); }
    }

    private static void Log(string message)
    {
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "macroengine.log");
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch { }
    }
}
