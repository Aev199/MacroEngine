using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MacroEngine.Core;
using MacroEngine.Modules;

namespace MacroEngine.UI;

/// <summary>
/// Tray-resident application controller.
/// Owns the tray icon with Start/Stop/Reload/Settings/Quit menu and
/// coordinates the keyboard hook + input buffer + text expansion lifecycle.
/// </summary>
internal sealed class AppController : IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly TrayIcon _trayIcon;
    private readonly NativeMenuItem _startStopItem;
    private readonly NativeMenuItem _statusItem;
    private readonly NativeMenuItem _autostartItem;

    private readonly KeyInterceptor _interceptor;
    private readonly InputBuffer _inputBuffer;
    private readonly TriggerConfig _config;
    private readonly MacroLibrary _macros;
    private readonly OverlayWindow _overlay;

    private SettingsWindow? _settingsWindow;

    private bool _isRunning;
    private uint _lastForegroundProcessId;
    private volatile bool _suppressToast;
    private IntPtr _foregroundHook = IntPtr.Zero;
    private NativeMethods.WinEventProc? _foregroundHookProc; // Keep delegate alive

    public AppController(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _lifetime = lifetime;

        // ── Config ───────────────────────────────────────────────
        string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "triggers.json");
        string macrosPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "macros.json");
        _config = new TriggerConfig(configPath);
        _macros = new MacroLibrary(macrosPath);

        Log("=== MacroEngine started ===");

        // ── Core components ──────────────────────────────────────
        _interceptor = new KeyInterceptor();
        _inputBuffer = new InputBuffer(maxLength: 64);

        _interceptor.KeyPressed += OnKeyPressed;
        _inputBuffer.TriggerMatched += OnTriggerMatched;
        _config.ConfigChanged += OnConfigChanged;

        var triggers = _config.Load();
        _inputBuffer.LoadTriggers(triggers);
        _config.StartWatching();

        _macros.Load();
        _macros.StartWatching();

        // ── Tray icon & menu ─────────────────────────────────────
        _statusItem = new NativeMenuItem("Статус: остановлен") { IsEnabled = false };
        _startStopItem = new NativeMenuItem("Запустить");
        _startStopItem.Click += (_, _) => { if (_isRunning) StopEngine(); else StartEngine(); };

        var reloadItem = new NativeMenuItem("Перезагрузить конфиг");
        reloadItem.Click += (_, _) => ReloadConfig();

        var settingsItem = new NativeMenuItem("Настройки…");
        settingsItem.Click += (_, _) => OpenSettings();

        _autostartItem = new NativeMenuItem("Автозапуск при входе в Windows")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = Autostart.IsEnabled()
        };
        _autostartItem.Click += (_, _) => ToggleAutostart();

        var quitItem = new NativeMenuItem("Выход");
        quitItem.Click += (_, _) => Quit();

        _trayIcon = new TrayIcon
        {
            Icon = AppIcon.Get(),
            ToolTipText = "MacroEngine — остановлен",
            Menu = new NativeMenu
            {
                Items =
                {
                    _statusItem,
                    new NativeMenuItemSeparator(),
                    _startStopItem,
                    reloadItem,
                    settingsItem,
                    new NativeMenuItemSeparator(),
                    _autostartItem,
                    new NativeMenuItemSeparator(),
                    quitItem
                }
            }
        };
        _trayIcon.Clicked += (_, _) => OpenSettings();
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _trayIcon });

        // ── Overlay HUD ──────────────────────────────────────────
        _overlay = new OverlayWindow();

        // ── Start automatically ──────────────────────────────────
        StartEngine();

        // ── First-run toast ──────────────────────────────────────
        string firstRunPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".firstrun");
        if (!File.Exists(firstRunPath))
        {
            try { File.WriteAllText(firstRunPath, ""); } catch { }
            _overlay.ShowToast("MacroEngine запущен — правый клик по иконке в трее", 5000);
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

            IntPtr hWnd = NativeMethods.GetForegroundWindow();
            NativeMethods.GetWindowThreadProcessId(hWnd, out _lastForegroundProcessId);

            _isRunning = true;
            UpdateUI();
            Log("Engine STARTED — hooks installed");
        }
        catch (Exception ex)
        {
            Log($"Engine START FAILED: {ex.Message}");
            _overlay.ShowToast($"Ошибка запуска перехвата: {ex.Message}", 6000);
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
        string status = _isRunning ? "активен" : "остановлен";
        _trayIcon.ToolTipText = $"MacroEngine — {status}";
        _statusItem.Header = $"Статус: {status}";
        _startStopItem.Header = _isRunning ? "Остановить" : "Запустить";
    }

    // ═══════════════════════════════════════════════════════════════
    //  Event Handlers
    // ═══════════════════════════════════════════════════════════════

    private void OnKeyPressed(KeyEventData args)
    {
        if (!_isRunning) return;
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

                if (!SystemHotkeys.IsSystem(combo))
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
                    if (SystemHotkeys.IsSystem(combo))
                    {
                        _inputBuffer.ResetLeader();
                        _overlay.HideOverlay();
                    }
                    else
                    {
                        string modPrefix = string.Join("+", mods);
                        bool fired = _inputBuffer.FeedLeaderKey(modPrefix, keyName, fp, out bool swallow);
                        if (swallow) KeyInterceptor.SuppressKey = true;
                        if (fired)
                        {
                            _overlay.ShowMatched(modPrefix, _inputBuffer.LastMatchedLeaderSeq);
                            Log($"  [LEADER] {modPrefix} + '{keyName}' matched");
                            return;
                        }
                        if (swallow)
                        {
                            _overlay.ShowLeader(modPrefix, _inputBuffer.CurrentLeaderSeq);
                            return;
                        }
                        _overlay.HideOverlay();
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

            case "open":
                RunOnStaThread(() =>
                {
                    KeyInterceptor.IsSuppressed = true;
                    try
                    {
                        TextExpander.EraseChars(eraseLen);
                        string path = TextExpander.ResolveTokens(entry.Value).Trim();
                        Process.Start("explorer.exe", path);
                    }
                    finally { KeyInterceptor.IsSuppressed = false; }
                });
                break;

            case "launch":
                RunOnStaThread(() =>
                {
                    KeyInterceptor.IsSuppressed = true;
                    try
                    {
                        TextExpander.EraseChars(eraseLen);
                        string cmd = TextExpander.ResolveTokens(entry.Value).Trim();
                        var psi = new ProcessStartInfo { UseShellExecute = true };
                        if (cmd.StartsWith('"'))
                        {
                            int end = cmd.IndexOf('"', 1);
                            psi.FileName = end > 0 ? cmd[1..end] : cmd.Trim('"');
                            if (end > 0 && end + 1 < cmd.Length)
                                psi.Arguments = cmd[(end + 1)..].TrimStart();
                        }
                        else
                        {
                            psi.FileName = cmd;
                        }
                        Process.Start(psi);
                    }
                    finally { KeyInterceptor.IsSuppressed = false; }
                });
                break;

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
        // Fired from the FileSystemWatcher thread — marshal UI work.
        _inputBuffer.LoadTriggers(newTriggers);
        if (!_suppressToast)
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                _overlay.ShowToast($"Конфиг перезагружен. Триггеров: {newTriggers.Count}"));
    }

    // ═══════════════════════════════════════════════════════════════
    //  Menu Handlers
    // ═══════════════════════════════════════════════════════════════

    private void ReloadConfig()
    {
        var triggers = _config.Load();
        _inputBuffer.LoadTriggers(triggers);
        _overlay.ShowToast($"Конфиг перезагружен вручную. Триггеров: {triggers.Count}");
    }

    private void ToggleAutostart()
    {
        try
        {
            bool enable = !Autostart.IsEnabled();
            Autostart.SetEnabled(enable);
            _autostartItem.IsChecked = enable;
            Log($"Autostart {(enable ? "enabled" : "disabled")}");
        }
        catch (Exception ex)
        {
            Log($"Autostart toggle failed: {ex.Message}");
            _autostartItem.IsChecked = Autostart.IsEnabled();
            _overlay.ShowToast($"Не удалось изменить автозапуск: {ex.Message}", 5000);
        }
    }

    private void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        // Suppress toast from the file watcher — the settings window saves directly.
        _suppressToast = true;
        _settingsWindow = new SettingsWindow(_config, _macros);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _macros.Load(); // pick up macro edits made in the window
            _suppressToast = false;
        };
        _settingsWindow.Show();
    }

    private void Quit()
    {
        Log("=== MacroEngine shutting down ===");
        Dispose();
        _lifetime.Shutdown();
    }

    private static void Log(string message) => AppLog.Write(message);

    public void Dispose()
    {
        StopEngine();
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
        _overlay.Close();
        _config.Dispose();
        _macros.Dispose();
        _interceptor.Dispose();
    }
}
