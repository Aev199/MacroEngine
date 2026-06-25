using MacroEngine.Core;
using MacroEngine.Modules;
using System.Diagnostics;
using System.Windows.Forms;

namespace MacroEngine.UI;

/// <summary>
/// System tray application context.
/// Provides the tray icon with Start/Stop/Reload/Quit menu
/// and coordinates the keyboard hook + input buffer + text expansion lifecycle.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _startStopItem;
    private readonly ToolStripMenuItem _statusItem;

    private readonly KeyInterceptor _interceptor;
    private readonly InputBuffer _inputBuffer;
    private readonly TriggerConfig _config;
    private readonly string _configPath;
    private readonly string _logPath;

    private bool _isRunning;
    private uint _lastForegroundProcessId;
    private volatile bool _suppressBalloon;
    private IntPtr _foregroundHook = IntPtr.Zero;
    private NativeMethods.WinEventProc? _foregroundHookProc; // Keep delegate alive

    public TrayApplicationContext()
    {
        // ── Config & log paths ──────────────────────────────────
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "triggers.json");
        _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "macroengine.log");
        _config = new TriggerConfig(_configPath);

        Log("=== MacroEngine started ===");

        // ── Core components ──────────────────────────────────────
        _interceptor = new KeyInterceptor();
        _inputBuffer = new InputBuffer(maxLength: 64);

        // Wire up: key press → buffer → trigger detection → expansion
        _interceptor.KeyPressed += OnKeyPressed;
        _inputBuffer.TriggerMatched += OnTriggerMatched;
        _config.ConfigChanged += OnConfigChanged;

        // ── Load initial triggers ────────────────────────────────
        var triggers = _config.Load();
        _inputBuffer.LoadTriggers(triggers);
        _config.StartWatching();

        // ── System Tray Icon ─────────────────────────────────────
        _trayIcon = new NotifyIcon
        {
            Icon = AppIcon.Get(),
            Text = "MacroEngine — остановлен",
            Visible = true
        };

        // Context menu
        var menu = new ContextMenuStrip();

        _statusItem = new ToolStripMenuItem("Статус: остановлен")
        {
            Enabled = false
        };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());

        _startStopItem = new ToolStripMenuItem("Запустить", null, OnStartStop);
        menu.Items.Add(_startStopItem);

        var reloadItem = new ToolStripMenuItem("Перезагрузить конфиг", null, OnReloadConfig);
        menu.Items.Add(reloadItem);

        var settingsItem = new ToolStripMenuItem("Редактор триггеров...", null, OnOpenSettings);
        menu.Items.Add(settingsItem);

        menu.Items.Add(new ToolStripSeparator());

        var quitItem = new ToolStripMenuItem("Выход", null, OnQuit);
        menu.Items.Add(quitItem);

        _trayIcon.ContextMenuStrip = menu;

        // ── Start automatically ──────────────────────────────────
        StartEngine();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Engine Start / Stop
    // ═══════════════════════════════════════════════════════════════

    private void StartEngine()
    {
        if (_isRunning) return;

        try
        {
            _interceptor.Start();

            // Install foreground change hook (event-driven, no polling)
            _foregroundHookProc = OnForegroundChanged;
            _foregroundHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _foregroundHookProc,
                0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT);

            // Initialize with current foreground process
            IntPtr hWnd = NativeMethods.GetForegroundWindow();
            NativeMethods.GetWindowThreadProcessId(hWnd, out _lastForegroundProcessId);

            _isRunning = true;
            UpdateUI();
            Log("Engine STARTED — hooks installed");
        }
        catch (Exception ex)
        {
            Log($"Engine START FAILED: {ex.Message}");
            MessageBox.Show(
                $"Не удалось запустить перехват клавиатуры:\n{ex.Message}",
                "MacroEngine — Ошибка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void StopEngine()
    {
        if (!_isRunning) return;

        _interceptor.Stop();
        if (_foregroundHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
        _inputBuffer.Clear();
        _isRunning = false;
        UpdateUI();
    }

    private void UpdateUI()
    {
        if (_isRunning)
        {
            _trayIcon.Text = "MacroEngine — активен";
            _statusItem.Text = "Статус: активен";
            _startStopItem.Text = "Остановить";
        }
        else
        {
            _trayIcon.Text = "MacroEngine — остановлен";
            _statusItem.Text = "Статус: остановлен";
            _startStopItem.Text = "Запустить";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Event Handlers
    // ═══════════════════════════════════════════════════════════════

    private void OnKeyPressed(KeyEventData args)
    {
        if (!_isRunning) return;

        // Skip suppressed events (during our own expansion)
        if (KeyInterceptor.IsSuppressed) return;

        // ── Hotkey / leader detection ─────────────────────────────
        // Fires for Ctrl/Alt combos and for standalone F1–F24.
        bool isFKey = args.VirtualKeyCode >= 0x70 && args.VirtualKeyCode <= 0x7B;
        if (args.Control || args.Alt || isFKey)
        {
            var mods = new List<string>();
            if (args.Control) mods.Add("Ctrl");
            if (args.Alt)     mods.Add("Alt");
            if (args.Shift)   mods.Add("Shift");

            string keyName = KeyInterceptor.VkToName(args.VirtualKeyCode);
            if (keyName.Length > 0)
            {
                string fp = WindowContext.GetContextFingerprint();

                // 1. Direct hotkey (Шорткат)
                string combo = mods.Count > 0
                    ? string.Join("+", mods) + "+" + keyName
                    : keyName;

                if (!IsSystemHotkey(combo))
                {
                    var entry = _inputBuffer.MatchHotkey(combo);
                    if (entry != null && WindowContext.MatchesContext(entry.Context, fp))
                    {
                        Log($"  [HOTKEY] {combo} → trigger='{entry.Trigger}' action={entry.Action}");
                        KeyInterceptor.SuppressKey = true;
                        OnTriggerMatched(entry);
                        return;
                    }
                }

                // 2. Leader chord (≥2 held modifiers) + typed rest sequence.
                //    System hotkeys have the highest priority — never feed or swallow
                //    them; let the OS handle the keystroke and abort any sequence.
                if (mods.Count >= 2)
                {
                    if (IsSystemHotkey(combo))
                    {
                        _inputBuffer.ResetLeader();
                    }
                    else
                    {
                        string modPrefix = string.Join("+", mods);
                        bool fired = _inputBuffer.FeedLeaderKey(modPrefix, keyName, fp, out bool swallow);
                        if (swallow) KeyInterceptor.SuppressKey = true;
                        if (fired)
                        {
                            Log($"  [LEADER] {modPrefix} + '{keyName}' matched");
                            return;
                        }
                        if (swallow) return; // sequence still building — don't feed the text buffer
                    }
                }
            }
        }

        // Ignore standalone modifier keys (both generic and left/right variants)
        if (args.VirtualKeyCode is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C  // generic
                               or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5)  // L/R variants
            return;

        // Log: character (if any), VK code, and current keyboard layout
        char c = args.ToChar();
        string layout = WindowContext.GetKeyboardLayoutTag();
        string ctxLog = WindowContext.GetContextFingerprint();
        if (c != '\0')
            Log($"  [KEY] '{c}' (U+{(int)c:X4}) VK=0x{args.VirtualKeyCode:X2} Shift={args.Shift} lay={layout} ctx={ctxLog}");
        else
            Log($"  [KEY] VK=0x{args.VirtualKeyCode:X2} SC=0x{args.ScanCode:X3} → no char (Shift={args.Shift}) lay={layout}");

        // Feed keystroke to input buffer with full window fingerprint for context matching
        string fingerprint = WindowContext.GetContextFingerprint();
        _inputBuffer.Feed(args, fingerprint);
    }

    /// <summary>Called by WinEventHook when foreground window changes.</summary>
    private void OnForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (!_isRunning) return;

        NativeMethods.GetWindowThreadProcessId(hWnd, out uint currentPid);
        if (currentPid != 0 && _lastForegroundProcessId != 0 && currentPid != _lastForegroundProcessId)
        {
            _inputBuffer.Clear();
        }
        if (currentPid != 0)
            _lastForegroundProcessId = currentPid;
    }

    private void OnTriggerMatched(TriggerEntry entry)
    {
        Log($"  [TRIGGER] matched! trigger='{entry.Trigger}' action={entry.Action} replacement='{entry.Value}'");

        switch (entry.Action.ToLowerInvariant())
        {
            case "script":
                RunOnStaThread(() => ScriptRunner.Run(entry.Value, entry.Trigger));
                break;

            case "richtext":
                RunOnStaThread(() => TextExpander.ExpandRichText(entry.Value, entry.Trigger.Length));
                break;

            case "lisp":
                RunOnStaThread(() => TextExpander.LoadLisp(entry.Value, entry.Trigger.Length));
                break;

            case "text":
            default:
                RunOnStaThread(() => TextExpander.Expand(entry.Value, entry.Trigger.Length));
                break;
        }
    }

    private void RunOnStaThread(Action action)
    {
        var t = new Thread(() =>
        {
            try { action(); Log($"  [EXPAND] done"); }
            catch (Exception ex) { Log($"  [ERROR] Expansion: {ex.Message}"); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
    }

    private void OnConfigChanged(List<TriggerEntry> newTriggers)
    {
        _inputBuffer.LoadTriggers(newTriggers);
        System.Diagnostics.Debug.WriteLine("[TrayApp] Config reloaded");

        if (!_suppressBalloon)
        {
            _trayIcon.ShowBalloonTip(
                2000,
                "MacroEngine",
                $"Конфиг перезагружен. Триггеров: {newTriggers.Count}",
                ToolTipIcon.Info);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Menu Handlers
    // ═══════════════════════════════════════════════════════════════

    private void OnStartStop(object? sender, EventArgs e)
    {
        if (_isRunning)
            StopEngine();
        else
            StartEngine();
    }

    private void OnReloadConfig(object? sender, EventArgs e)
    {
        var triggers = _config.Load();
        _inputBuffer.LoadTriggers(triggers);
        _trayIcon.ShowBalloonTip(
            2000,
            "MacroEngine",
            $"Конфиг перезагружен вручную. Триггеров: {triggers.Count}",
            ToolTipIcon.Info);
    }

    private void OnOpenSettings(object? sender, EventArgs e)
    {
        // Suppress balloon tip from file watcher — settings form shows its own confirmation.
        _suppressBalloon = true;
        try
        {
            using var form = new SettingsForm(_config);
            form.ShowDialog();
        }
        finally
        {
            _suppressBalloon = false;
        }
    }

    private void OnQuit(object? sender, EventArgs e)
    {
        Log("=== MacroEngine shutting down ===");
        StopEngine();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _config.Dispose();
        _interceptor.Dispose();
        Application.Exit();
    }

    private static bool IsSystemHotkey(string combo) => SystemHotkeys.IsSystem(combo);

    // ═══════════════════════════════════════════════════════════════
    //  Logging
    // ═══════════════════════════════════════════════════════════════

    private void Log(string message)
    {
        try
        {
            string line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
            Debug.WriteLine(line);
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }
        catch
        {
            // Never crash because of logging
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Cleanup
    // ═══════════════════════════════════════════════════════════════

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopEngine();
            _trayIcon?.Dispose();
            _config?.Dispose();
            _interceptor?.Dispose();
        }
        base.Dispose(disposing);
    }
}