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
    private readonly NativeMenuItem _diagnosticItem;
    private readonly NativeMenuItem _cancelItem;

    private readonly KeyInterceptor _interceptor;
    private readonly InputBuffer _inputBuffer;
    private readonly TriggerConfig _config;
    private readonly MacroLibrary _macros;
    private readonly ExecutionQueue _executionQueue;
    private readonly OverlayWindow _overlay;

    private SettingsWindow? _settingsWindow;
    private bool _isRunning;
    private readonly ForegroundWindowTracker _foregroundWindow = new();
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

        _diagnosticItem = new NativeMenuItem("Диагностическое логирование")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = AppLog.DiagnosticEnabled
        };
        _diagnosticItem.Click += (_, _) => ToggleDiagnosticLogging();

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
                    _diagnosticItem,
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

        _executionQueue.JobCancelled += (_, reason) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _cancelItem.IsEnabled = false;
                UpdateUI();
                _overlay.ShowToast(
                    string.IsNullOrWhiteSpace(reason) ? "Текущее действие остановлено" : reason,
                    5000);
            });

        _executionQueue.JobFailed += (description, ex) =>
        {
            // The message may include a user path, command argument or macro
            // fragment. Keep the persisted normal log privacy-safe.
            AppLog.Write($"Action '{description}' failed: {ex.GetType().Name}");
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
            if (_foregroundHook == IntPtr.Zero)
                throw new InvalidOperationException("Windows не установила foreground hook.");

            IntPtr hWnd = NativeMethods.GetForegroundWindow();
            _foregroundWindow.Reset(hWnd);

            _isRunning = true;
            UpdateUI();
            AppLog.Write("Engine started; hooks installed");
        }
        catch (Exception ex)
        {
            _interceptor.Stop();
            if (_foregroundHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_foregroundHook);
                _foregroundHook = IntPtr.Zero;
            }
            _foregroundHookProc = null;
            _isRunning = false;
            UpdateUI();
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
        _foregroundHookProc = null;
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

        if (_foregroundWindow.Update(hWnd))
            _inputBuffer.Clear();
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
        AutomationTarget target = AutomationTarget.Capture();

        if (!target.IsCaptured)
        {
            AppLog.Write("Action rejected: foreground window could not be captured");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                _overlay.ShowToast("Не удалось определить активное окно — запуск отменён", 5000));
            return;
        }

        AppLog.Write($"Action requested: type={action}; activation={activation}");

        bool accepted = _executionQueue.TryEnqueue(action, cancellationToken =>
        {
            bool previousSuppression = KeyInterceptor.IsSuppressed;
            KeyInterceptor.IsSuppressed = true;
            try
            {
                target.ThrowIfNotForeground(cancellationToken);
                if (activation != "text")
                    WaitForActivationModifiersReleased(target, cancellationToken);
                ExecuteAction(entry, action, eraseLength, target, cancellationToken);
            }
            finally
            {
                KeyInterceptor.IsSuppressed = previousSuppression;
            }
        });

        if (!accepted)
        {
            AppLog.Write("Execution worker is busy; action rejected");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                _overlay.ShowToast("MacroEngine занят — новый запуск пропущен", 4000));
        }
    }
    private void ExecuteAction(
        TriggerEntry entry,
        string action,
        int eraseLength,
        AutomationTarget target,
        CancellationToken cancellationToken)
    {
        switch (action)
        {
            case "script":
                TextExpander.EraseChars(eraseLength, target, cancellationToken);
                ScriptRunner.Run(entry.Value, entry.Trigger, cancellationToken);
                break;
            case "richtext":
                TextExpander.ExpandRichText(entry.Value, eraseLength, target, cancellationToken);
                break;
            case "lisp":
                TextExpander.LoadLisp(entry.Value, eraseLength, target, cancellationToken);
                break;
            case "macro":
            {
                string script = _macros.TryGet(entry.Value.Trim(), out var definition)
                    ? definition.Script
                    : entry.Value;
                MacroRunner.Run(script, eraseLength, target, cancellationToken);
                break;
            }
            case "open":
                OpenPath(entry.Value, eraseLength, target, cancellationToken);
                break;
            case "launch":
                Launch(entry.Value, eraseLength, target, cancellationToken);
                break;
            case "text":
            default:
                TextExpander.Expand(entry.Value, eraseLength, target, cancellationToken);
                break;
        }
    }
    private static void OpenPath(
        string rawPath,
        int eraseLength,
        AutomationTarget target,
        CancellationToken cancellationToken)
    {
        bool previousSuppression = KeyInterceptor.IsSuppressed;
        KeyInterceptor.IsSuppressed = true;
        try
        {
            target.ThrowIfNotForeground(cancellationToken);
            TextExpander.EraseChars(eraseLength, target, cancellationToken);
            string path = TextExpander.ResolveTokens(rawPath, target, cancellationToken).Trim();
            cancellationToken.ThrowIfCancellationRequested();
            Process? process = Process.Start("explorer.exe", path);
            if (process == null)
                throw new InvalidOperationException("Windows не смогла открыть указанный путь.");
            process.Dispose();
        }
        finally
        {
            KeyInterceptor.IsSuppressed = previousSuppression;
        }
    }

    private static void Launch(
        string rawCommand,
        int eraseLength,
        AutomationTarget target,
        CancellationToken cancellationToken)
    {
        bool previousSuppression = KeyInterceptor.IsSuppressed;
        KeyInterceptor.IsSuppressed = true;
        try
        {
            target.ThrowIfNotForeground(cancellationToken);
            TextExpander.EraseChars(eraseLength, target, cancellationToken);
            string command = TextExpander.ResolveTokens(rawCommand, target, cancellationToken).Trim();
            cancellationToken.ThrowIfCancellationRequested();

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
        finally
        {
            KeyInterceptor.IsSuppressed = previousSuppression;
        }
    }

    private static void WaitForActivationModifiersReleased(
        AutomationTarget target,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while (IsActivationModifierDown())
        {
            target.ThrowIfNotForeground(cancellationToken);
            if (DateTime.UtcNow >= deadline)
            {
                throw new OperationCanceledException(
                    "Сочетание удерживается слишком долго — запуск отменён.",
                    cancellationToken);
            }

            if (cancellationToken.WaitHandle.WaitOne(10))
                cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static bool IsActivationModifierDown() =>
        (NativeMethods.GetAsyncKeyState(0x11) & 0x8000) != 0
        || (NativeMethods.GetAsyncKeyState(0x12) & 0x8000) != 0
        || (NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0
        || (NativeMethods.GetAsyncKeyState(0x5B) & 0x8000) != 0
        || (NativeMethods.GetAsyncKeyState(0x5C) & 0x8000) != 0;

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

    private void ToggleDiagnosticLogging()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.StateDirectory);
            bool enable = !File.Exists(AppPaths.DiagnosticMarker);
            if (enable)
                File.WriteAllText(AppPaths.DiagnosticMarker, "");
            else
                File.Delete(AppPaths.DiagnosticMarker);

            _diagnosticItem.IsChecked = AppLog.DiagnosticEnabled;
            AppLog.Write($"Diagnostic logging {(AppLog.DiagnosticEnabled ? "enabled" : "disabled")}");
            _overlay.ShowToast(AppLog.DiagnosticEnabled
                ? "Диагностическое логирование включено. Оно может содержать сведения о клавишах и окнах."
                : "Диагностическое логирование выключено.",
                AppLog.DiagnosticEnabled ? 6500 : 3000);
        }
        catch (Exception ex)
        {
            _diagnosticItem.IsChecked = AppLog.DiagnosticEnabled;
            AppLog.Write($"Diagnostic logging toggle failed: {ex.GetType().Name}: {ex.Message}");
            _overlay.ShowToast($"Не удалось изменить режим диагностики: {ex.Message}", 5000);
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
