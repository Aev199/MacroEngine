using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MacroEngine.Core;
using MacroEngine.Modules;

namespace MacroEngine.UI;

/// <summary>
/// Tray-resident application controller. Keyboard detection stays on the hook
/// thread; all resulting automation is serialized by <see cref="ExecutionQueue"/>.
/// </summary>
internal sealed class AppController : IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly TrayIcon _trayIcon;
    private readonly NativeMenuItem _startStopItem;
    private readonly NativeMenuItem _statusItem;
    private readonly NativeMenuItem _autostartItem;
    private readonly NativeMenuItem _cancelItem;

    private readonly KeyInterceptor _interceptor;
    private readonly InputBuffer _inputBuffer;
    private readonly TriggerConfig _config;
    private readonly MacroLibrary _macros;
    private readonly ExecutionQueue _executionQueue;
    private readonly OverlayWindow _overlay;

    private SettingsWindow? _settingsWindow;
    private bool _isRunning;
    private uint _lastForegroundProcessId;
    private volatile bool _suppressToast;
    private IntPtr _foregroundHook = IntPtr.Zero;
    private NativeMethods.WinEventProc? _foregroundHookProc;

    public AppController(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _lifetime = lifetime;

        AppPaths.Initialize();
        _config = new TriggerConfig(AppPaths.TriggersFile);
        _macros = new MacroLibrary(AppPaths.MacrosFile);

        AppLog.Write("=== MacroEngine started ===");
        AppLog.Write($"Data mode: {(AppPaths.IsPortable ? "portable" : "LocalAppData")}");

        _interceptor = new KeyInterceptor();
        _inputBuffer = new InputBuffer(maxLength: 64);
        _executionQueue = new ExecutionQueue();

        _interceptor.KeyPressed += OnKeyPressed;
        _inputBuffer.TriggerMatched += OnTriggerMatched;
        _config.ConfigChanged += OnConfigChanged;

        var triggers = _config.Load();
        _inputBuffer.LoadTriggers(triggers);
        _config.StartWatching();
        _macros.Load();
        _macros.StartWatching();

        _statusItem = new NativeMenuItem("Статус: остановлен") { IsEnabled = false };
        _startStopItem = new NativeMenuItem("Запустить");
        _startStopItem.Click += (_, _) => { if (_isRunning) StopEngine(); else StartEngine(); };

        var reloadItem = new NativeMenuItem("Перезагрузить конфиг");
        reloadItem.Click += (_, _) => ReloadConfig();

        var settingsItem = new NativeMenuItem("Настройки…");
        settingsItem.Click += (_, _) => OpenSettings();

        _cancelItem = new NativeMenuItem("Остановить текущее действие") { IsEnabled = false };
        _cancelItem.Click += (_, _) => _executionQueue.CancelAll();

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
                    _cancelItem,
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

        _overlay = new OverlayWindow();
        WireExecutionNotifications();
        StartEngine();

        if (!File.Exists(AppPaths.FirstRunMarker))
        {
            try { File.WriteAllText(AppPaths.FirstRunMarker, ""); } catch { }
            _overlay.ShowToast("MacroEngine запущен — правый клик по иконке в трее", 5000);
        }
    }

    private void WireExecutionNotifications()
    {
        _executionQueue.JobStarted += description =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _cancelItem.IsEnabled = true;
                _statusItem.Header = $"Выполняется: {description}";
            });

        _executionQueue.JobCompleted += _ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _cancelItem.IsEnabled = false;
                UpdateUI();
            });

        _executionQueue.JobCancelled += _ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _cancelItem.IsEnabled = false;
                UpdateUI();
                _overlay.ShowToast("Текущее действие остановлено");
            });

        _executionQueue.JobFailed += (description, ex) =>
        {
            AppLog.Write($"Action '{description}' failed: {ex.GetType().Name}: {ex.Message}");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _cancelItem.IsEnabled = false;
                UpdateUI();
                _overlay.ShowToast($"Ошибка действия «{description}»: {ex.Message}", 6000);
            });
        };
    }

    private void StartEngine()
    {
        if (_isRunning) return;

        try
        {
            _interceptor.Start();
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
            AppLog.Write("Engine started; hooks installed");
        }
        catch (Exception ex)
        {
            AppLog.Write($"Engine start failed: {ex.GetType().Name}: {ex.Message}");
            _overlay.ShowToast($"Ошибка запуска перехвата: {ex.Message}", 6000);
        }
    }

    private void StopEngine()
    {
        if (!_isRunning) return;

        _executionQueue.CancelAll();
        _interceptor.Stop();
        if (_foregroundHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
        _inputBuffer.Clear();
        _isRunning = false;
        UpdateUI();
        AppLog.Write("Engine stopped");
    }

    private void UpdateUI()
    {
        string status = _isRunning ? "активен" : "остановлен";
        _trayIcon.ToolTipText = $"MacroEngine — {status}";
        _statusItem.Header = $"Статус: {status}";
        _startStopItem.Header = _isRunning ? "Остановить" : "Запустить";
    }

    private void OnKeyPressed(KeyEventData args)
    {
        if (!_isRunning || KeyInterceptor.IsSuppressed) return;

        bool isFKey = args.VirtualKeyCode is >= 0x70 and <= 0x7B;
        if (args.Control || args.Alt || isFKey)
        {
            var mods = new List<string>();
            if (args.Control) mods.Add("Ctrl");
            if (args.Alt) mods.Add("Alt");
            if (args.Shift) mods.Add("Shift");

            string keyName = KeyInterceptor.VkToName(args.VirtualKeyCode);
            if (keyName.Length > 0)
            {
                string fingerprint = WindowContext.GetContextFingerprint();
                string combo = mods.Count > 0
                    ? string.Join("+", mods) + "+" + keyName
                    : keyName;

                if (!SystemHotkeys.IsSystem(combo))
                {
                    var entry = _inputBuffer.MatchHotkey(combo);
                    if (entry != null && WindowContext.MatchesContext(entry.Context, fingerprint))
                    {
                        AppLog.Diagnostic($"Hotkey matched: {combo}; action={entry.Action}");
                        KeyInterceptor.SuppressKey = true;
                        OnTriggerMatched(entry);
                        return;
                    }
                }

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
                        bool fired = _inputBuffer.FeedLeaderKey(modPrefix, keyName, fingerprint, out bool swallow);
                        if (swallow) KeyInterceptor.SuppressKey = true;
                        if (fired)
                        {
                            _overlay.ShowMatched(modPrefix, _inputBuffer.LastMatchedLeaderSeq);
                            AppLog.Diagnostic($"Leader matched: {modPrefix}; action queued");
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

        if (args.VirtualKeyCode is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C
                               or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5)
            return;

        char character = args.ToChar();
        if (character != '\0')
        {
            AppLog.Diagnostic(
                $"Key U+{(int)character:X4}; VK=0x{args.VirtualKeyCode:X2}; " +
                $"layout={WindowContext.GetKeyboardLayoutTag()}; context={WindowContext.GetContextFingerprint()}");
        }

        _inputBuffer.Feed(args, WindowContext.GetContextFingerprint());
    }

    private void OnForegroundChanged(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hWnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (!_isRunning) return;

        NativeMethods.GetWindowThreadProcessId(hWnd, out uint currentPid);
        if (currentPid != 0 && _lastForegroundProcessId != 0 && currentPid != _lastForegroundProcessId)
            _inputBuffer.Clear();
        if (currentPid != 0)
            _lastForegroundProcessId = currentPid;
    }

    private void OnTriggerMatched(TriggerEntry entry)
    {
        string action = string.IsNullOrWhiteSpace(entry.Action)
            ? "text"
            : entry.Action.Trim().ToLowerInvariant();
        string activation = !string.IsNullOrWhiteSpace(entry.Leader)
            ? "leader"
            : !string.IsNullOrWhiteSpace(entry.Hotkey)
                ? "hotkey"
                : "text";
        int eraseLength = activation == "text" ? entry.Trigger.Length : 0;
        IntPtr targetWindow = NativeMethods.GetForegroundWindow();

        AppLog.Write($"Action queued: type={action}; activation={activation}");

        bool accepted = _executionQueue.TryEnqueue(action, cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (targetWindow != IntPtr.Zero)
                NativeMethods.SetForegroundWindow(targetWindow);

            ExecuteAction(entry, action, eraseLength, cancellationToken);
        });

        if (!accepted)
        {
            AppLog.Write("Execution queue is full; action rejected");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                _overlay.ShowToast("Очередь действий заполнена — запуск пропущен", 5000));
        }
    }

    private void ExecuteAction(
        TriggerEntry entry,
        string action,
        int eraseLength,
        CancellationToken cancellationToken)
    {
        switch (action)
        {
            case "script":
                ScriptRunner.Run(entry.Value, entry.Trigger, cancellationToken);
                break;
            case "richtext":
                cancellationToken.ThrowIfCancellationRequested();
                TextExpander.ExpandRichText(entry.Value, eraseLength);
                break;
            case "lisp":
                cancellationToken.ThrowIfCancellationRequested();
                TextExpander.LoadLisp(entry.Value, eraseLength);
                break;
            case "macro":
            {
                string script = _macros.TryGet(entry.Value.Trim(), out var definition)
                    ? definition.Script
                    : entry.Value;
                MacroRunner.Run(script, eraseLength, cancellationToken);
                break;
            }
            case "open":
                OpenPath(entry.Value, eraseLength, cancellationToken);
                break;
            case "launch":
                Launch(entry.Value, eraseLength, cancellationToken);
                break;
            case "text":
            default:
                cancellationToken.ThrowIfCancellationRequested();
                TextExpander.Expand(entry.Value, eraseLength);
                break;
        }
    }

    private static void OpenPath(string rawPath, int eraseLength, CancellationToken cancellationToken)
    {
        KeyInterceptor.IsSuppressed = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            TextExpander.EraseChars(eraseLength);
            string path = TextExpander.ResolveTokens(rawPath).Trim();
            cancellationToken.ThrowIfCancellationRequested();
            Process.Start("explorer.exe", path);
        }
        finally
        {
            KeyInterceptor.IsSuppressed = false;
        }
    }

    private static void Launch(string rawCommand, int eraseLength, CancellationToken cancellationToken)
    {
        KeyInterceptor.IsSuppressed = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            TextExpander.EraseChars(eraseLength);
            string command = TextExpander.ResolveTokens(rawCommand).Trim();
            cancellationToken.ThrowIfCancellationRequested();

            var startInfo = new ProcessStartInfo { UseShellExecute = true };
            if (command.StartsWith('"'))
            {
                int end = command.IndexOf('"', 1);
                startInfo.FileName = end > 0 ? command[1..end] : command.Trim('"');
                if (end > 0 && end + 1 < command.Length)
                    startInfo.Arguments = command[(end + 1)..].TrimStart();
            }
            else
            {
                startInfo.FileName = command;
            }
            Process.Start(startInfo);
        }
        finally
        {
            KeyInterceptor.IsSuppressed = false;
        }
    }

    private void OnConfigChanged(List<TriggerEntry> newTriggers)
    {
        _inputBuffer.LoadTriggers(newTriggers);
        if (!_suppressToast)
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                _overlay.ShowToast($"Конфиг перезагружен. Триггеров: {newTriggers.Count}"));
    }

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
            AppLog.Write($"Autostart {(enable ? "enabled" : "disabled")}");
        }
        catch (Exception ex)
        {
            AppLog.Write($"Autostart toggle failed: {ex.GetType().Name}: {ex.Message}");
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

        _suppressToast = true;
        _settingsWindow = new SettingsWindow(_config, _macros);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _macros.Load();
            _suppressToast = false;
        };
        _settingsWindow.Show();
    }

    private void Quit()
    {
        AppLog.Write("=== MacroEngine shutting down ===");
        Dispose();
        _lifetime.Shutdown();
    }

    public void Dispose()
    {
        StopEngine();
        _executionQueue.Dispose();
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
        _overlay.Close();
        _config.Dispose();
        _macros.Dispose();
        _interceptor.Dispose();
    }
}
