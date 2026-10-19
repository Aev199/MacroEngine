using System.Diagnostics;
using MacroEngine.Core;

namespace MacroEngine.Modules;

/// <summary>
/// Executes a newline-separated macro script. Jobs are serialized by
/// <see cref="ExecutionQueue"/> and remain bound to the window that triggered them.
/// </summary>
internal static class MacroRunner
{
    private enum MouseButton { Left, Right }

    public static void Run(
        string script,
        int eraseLen,
        AutomationTarget target,
        CancellationToken cancellationToken = default)
    {
        bool previousSuppression = KeyInterceptor.IsSuppressed;
        KeyInterceptor.IsSuppressed = true;
        try
        {
            target.ThrowIfNotForeground(cancellationToken);
            TextExpander.EraseChars(eraseLen, target, cancellationToken);

            foreach (var rawLine in script.Replace("\r", "").Split('\n'))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                target.ThrowIfNotForeground(cancellationToken);

                int sp = line.IndexOf(' ');
                string verb = (sp < 0 ? line : line[..sp]).ToLowerInvariant();
                string arg = sp < 0 ? "" : line[(sp + 1)..].Trim();

                try
                {
                    Execute(verb, arg, target, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Do not persist step arguments or exception messages: they
                    // may contain user macro text, paths or command arguments.
                    AppLog.Write($"Macro step '{verb}' failed: {ex.GetType().Name}");
                    throw new InvalidOperationException($"Ошибка шага макроса «{verb}»: {ex.Message}", ex);
                }
            }
        }
        finally
        {
            KeyInterceptor.IsSuppressed = previousSuppression;
        }
    }

    private static void Execute(
        string verb,
        string arg,
        AutomationTarget target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (verb)
        {
            case "type":
                TextExpander.TypeText(arg, target, cancellationToken);
                break;
            case "key":
                target.ThrowIfNotForeground(cancellationToken);
                SendCombo(arg);
                break;
            case "click":
                MouseClick(arg, MouseButton.Left, doubleClick: false, target, cancellationToken);
                break;
            case "dclick":
                MouseClick(arg, MouseButton.Left, doubleClick: true, target, cancellationToken);
                break;
            case "rclick":
                MouseClick(arg, MouseButton.Right, doubleClick: false, target, cancellationToken);
                break;
            case "run":
                target.ThrowIfNotForeground(cancellationToken);
                Launch(arg);
                break;
            case "sleep":
                if (!int.TryParse(arg, out int ms) || ms < 0)
                    throw new FormatException($"Некорректная задержка: {arg}");
                Delay(Math.Min(ms, 60_000), cancellationToken);
                break;
            default:
                throw new FormatException($"Неизвестная команда макроса: {verb}");
        }
    }

    private static void Delay(int milliseconds, CancellationToken cancellationToken)
    {
        if (milliseconds <= 0) return;
        if (cancellationToken.WaitHandle.WaitOne(milliseconds))
            cancellationToken.ThrowIfCancellationRequested();
    }

    // ── Keyboard ────────────────────────────────────────────────────

    private static void SendCombo(string combo)
    {
        var parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            throw new FormatException("Пустое сочетание клавиш.");

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
                default:
                    ushort parsed = NameToVk(p);
                    if (parsed == 0)
                        throw new FormatException($"Неизвестная клавиша: {p}");
                    if (main != 0)
                        throw new FormatException("Сочетание должно содержать только одну основную клавишу.");
                    main = parsed;
                    break;
            }
        }

        if (main == 0)
            throw new FormatException("Сочетание должно содержать основную клавишу.");

        var inputs = new List<NativeMethods.INPUT>();
        foreach (var modifier in mods) inputs.Add(KeyInput(modifier, up: false));
        inputs.Add(KeyInput(main, up: false));
        inputs.Add(KeyInput(main, up: true));
        for (int i = mods.Count - 1; i >= 0; i--) inputs.Add(KeyInput(mods[i], up: true));

        InputInjection.Send(inputs);
    }

    private static NativeMethods.INPUT KeyInput(ushort vk, bool up)
    {
        uint scan = NativeMethods.MapVirtualKey(vk, NativeMethods.MAPVK_VK_TO_VSC);
        bool extended = InputInjection.RequiresExtendedKeyFlag(vk);
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

    internal static ushort NameToVk(string name)
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

    private static void MouseClick(
        string arg,
        MouseButton button,
        bool doubleClick,
        AutomationTarget target,
        CancellationToken cancellationToken)
    {
        target.ThrowIfNotForeground(cancellationToken);

        if (arg.Length > 0)
        {
            var xy = arg.Split(',', StringSplitOptions.TrimEntries);
            if (xy.Length != 2 || !int.TryParse(xy[0], out int x) || !int.TryParse(xy[1], out int y))
                throw new FormatException($"Координаты мыши должны иметь формат x,y: {arg}");

            InputInjection.SetCursorPosition(x, y);
            Delay(20, cancellationToken);
            target.ThrowIfNotForeground(cancellationToken);
        }

        SendMouseClick(button);
        if (doubleClick)
        {
            Delay(40, cancellationToken);
            target.ThrowIfNotForeground(cancellationToken);
            SendMouseClick(button);
        }
    }

    private static void SendMouseClick(MouseButton button)
    {
        uint down = button == MouseButton.Left ? NativeMethods.MOUSEEVENTF_LEFTDOWN : NativeMethods.MOUSEEVENTF_RIGHTDOWN;
        uint up   = button == MouseButton.Left ? NativeMethods.MOUSEEVENTF_LEFTUP   : NativeMethods.MOUSEEVENTF_RIGHTUP;
        InputInjection.Send(new[] { MouseInput(down), MouseInput(up) });
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
        CommandLineParser.Split(command, out string fileName, out string arguments);
        Process? process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = true
        });

        if (process == null)
            throw new InvalidOperationException("Windows не смогла запустить указанную команду.");

        process.Dispose();
    }
}
