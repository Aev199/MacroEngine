using MacroEngine.Core;
using MacroEngine.Modules;
using System.Windows.Forms;

namespace MacroEngine.UI;

/// <summary>
/// System tray application context.
/// Provides the tray icon (double-click → editor, menu: editor / pause / autostart / quit)
/// and coordinates the keyboard hook + input buffer + text expansion lifecycle.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _pauseItem;

    private readonly KeyInterceptor _interceptor;
    private readonly InputBuffer _inputBuffer;
    private readonly TriggerConfig _config;
    private readonly MacroLibrary _macros;
    private readonly string _configPath;
    private readonly string _macrosPath;
    private readonly LeaderOverlayForm _leaderOverlay;
    private SettingsForm? _settingsForm;
    private int _triggerCount;

    private bool _isRunning;
    private uint _lastForegroundProcessId;
    private volatile bool _suppressBalloon;
    private IntPtr _foregroundHook = IntPtr.Zero;
    private NativeMethods.WinEventProc? _foregroundHookProc; // Keep delegate alive

    public TrayApplicationContext()
    {
        // ── Config & log paths ──────────────────────────────────
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "triggers.json");
        _macrosPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "macros.json");
        _config = new TriggerConfig(_configPath);
        _macros = new MacroLibrary(_macrosPath);

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
        _triggerCount = triggers.Count;
        _config.StartWatching();

        // ── Load named macros ────────────────────────────────────
        _macros.Load();
        _macros.StartWatching();

        // ── System Tray Icon ─────────────────────────────────────
        _trayIcon = new NotifyIcon
        {
            Icon = AppIcon.Get(),
            Text = "MacroEngine — на паузе",
            Visible = true
        };
        _trayIcon.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) OnOpenSettings(this, EventArgs.Empty); };

        // Context menu. Config hot-reloads on its own, so there is no "reload" item;
        // the engine state lives in the icon, the tooltip and the pause item.
        var menu = new ContextMenuStrip { Renderer = new GbMenuRenderer(), Font = Theme.Body, ShowImageMargin = false };

        var settingsItem = new ToolStripMenuItem("Открыть редактор", null, OnOpenSettings) { Font = Theme.BodyBold };
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());

        _pauseItem = new ToolStripMenuItem("Пауза", null, OnStartStop) { CheckOnClick = false };
        menu.Items.Add(_pauseItem);

        var autostartItem = new ToolStripMenuItem("Автозапуск при входе в Windows", null, OnToggleAutostart)
        {
            CheckOnClick = true,
            Checked = Autostart.IsEnabled()
        };
        menu.Items.Add(autostartItem);

        menu.Items.Add(new ToolStripSeparator());

        var quitItem = new ToolStripMenuItem("Выход", null, OnQuit);
        menu.Items.Add(quitItem);

        _trayIcon.ContextMenuStrip = menu;
        menu.Opening += (_, _) => _pauseItem.Checked = !_isRunning;

        // ── Leader overlay ───────────────────────────────────────
        _leaderOverlay = new LeaderOverlayForm();

        // ── Start automatically ──────────────────────────────────
        StartEngine();

        // ── First-run balloon ────────────────────────────────────
        string firstRunPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".firstrun");
        if (!File.Exists(firstRunPath))
        {
            try { File.WriteAllText(firstRunPath, ""); } catch { }
            _trayIcon.ShowBalloonTip(5000, "MacroEngine",
                "MacroEngine запущен. Двойной клик по иконке в трее — редактор триггеров.",
                ToolTipIcon.Info);
        }
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
        // NotifyIcon.Text is limited to 63 characters.
        _trayIcon.Text = _isRunning
            ? $"MacroEngine — активен · триггеров: {_triggerCount}"
            : "MacroEngine — на паузе";
        _trayIcon.Icon = _isRunning ? AppIcon.Get() : AppIcon.GetPaused();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Event Handlers
    // ═══════════════════════════════════════════════════════════════

    private void OnKeyPressed(KeyEventData args)
    {
        if (!_isRunning) return;

        // Skip suppressed events (during our own expansion)
        if (KeyInterceptor.IsSuppressed) return;

        // Never expand inside our own windows — typing "!tel" into the editor's
        // trigger field must stay "!tel", and recording a hotkey must not fire it.
        NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out uint fgPid);
        if (fgPid == (uint)Environment.ProcessId) return;

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
                        _leaderOverlay.HideOverlay();
                    }
                    else
                    {
                        string modPrefix = string.Join("+", mods);
                        bool fired = _inputBuffer.FeedLeaderKey(modPrefix, keyName, fp, out bool swallow);
                        if (swallow) KeyInterceptor.SuppressKey = true;
                        if (fired)
                        {
                            _leaderOverlay.ShowMatched(modPrefix, _inputBuffer.LastMatchedLeaderSeq);
                            Log($"  [LEADER] {modPrefix} + '{keyName}' matched");
                            return;
                        }
                        if (swallow)
                        {
                            _leaderOverlay.ShowLeader(modPrefix, _inputBuffer.CurrentLeaderSeq);
                            return;
                        }
                        _leaderOverlay.HideOverlay();
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

        // Leader and shortcut keys are swallowed before they reach the target app,
        // so there is nothing in the document to erase. Only typed-text triggers
        // leave their trigger characters behind.
        int eraseLen = string.IsNullOrEmpty(entry.Leader) ? entry.Trigger.Length : 0;

        switch (entry.Action.ToLowerInvariant())
        {
            case "script":
                RunOnStaThread(() => ScriptRunner.Run(entry.Value, entry.Trigger));
                break;

            case "richtext":
                RunOnStaThread(() => TextExpander.ExpandRichText(entry.Value, eraseLen));
                break;

            case "lisp":
                RunOnStaThread(() => TextExpander.LoadLisp(entry.Value, eraseLen));
                break;

            case "macro":
            {
                // entry.Value is a macro name; fall back to treating it as an inline script.
                string script = _macros.TryGet(entry.Value.Trim(), out var def) ? def.Script : entry.Value;
                RunOnStaThread(() => MacroRunner.Run(script, eraseLen));
                break;
            }

            case "text":
            default:
                RunOnStaThread(() => TextExpander.Expand(entry.Value, eraseLen));
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
        _triggerCount = newTriggers.Count;
        System.Diagnostics.Debug.WriteLine("[TrayApp] Config reloaded");
        UpdateUI(); // tooltip count; touches only the NotifyIcon, safe from the watcher thread

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

    private void OnToggleAutostart(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem item) return;
        try
        {
            Autostart.SetEnabled(item.Checked);
            Log($"Autostart {(item.Checked ? "enabled" : "disabled")}");
        }
        catch (Exception ex)
        {
            Log($"Autostart toggle failed: {ex.Message}");
            item.Checked = Autostart.IsEnabled();
            MessageBox.Show($"Не удалось изменить автозапуск:\n{ex.Message}",
                "MacroEngine", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnOpenSettings(object? sender, EventArgs e)
    {
        // One editor at a time — a second click just brings it forward.
        if (_settingsForm != null)
        {
            if (_settingsForm.WindowState == FormWindowState.Minimized)
                _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Activate();
            return;
        }

        // Suppress balloon tip from file watcher — the editor shows its own confirmation.
        _suppressBalloon = true;
        _settingsForm = new SettingsForm(_config, _macros);
        _settingsForm.FormClosed += (_, _) =>
        {
            // Pick up any macro edits made in the form.
            _macros.Load();
            _suppressBalloon = false;
            _settingsForm = null; // a modeless form disposes itself on close
        };
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void OnQuit(object? sender, EventArgs e)
    {
        Log("=== MacroEngine shutting down ===");
        _settingsForm?.Close();
        if (_settingsForm != null) return; // user cancelled closing the editor with unsaved changes
        StopEngine();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _leaderOverlay.Dispose();
        _config.Dispose();
        _macros.Dispose();
        _interceptor.Dispose();
        Application.Exit();
    }

    private static bool IsSystemHotkey(string combo) => SystemHotkeys.IsSystem(combo);

    // ═══════════════════════════════════════════════════════════════
    //  Logging
    // ═══════════════════════════════════════════════════════════════

    private static void Log(string message) => AppLog.Write(message);

    // ═══════════════════════════════════════════════════════════════
    //  Cleanup
    // ═══════════════════════════════════════════════════════════════

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopEngine();
            _trayIcon?.Dispose();
            _leaderOverlay?.Dispose();
            _config?.Dispose();
            _macros?.Dispose();
            _interceptor?.Dispose();
        }
        base.Dispose(disposing);
    }
}
