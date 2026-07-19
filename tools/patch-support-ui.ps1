$ErrorActionPreference = 'Stop'

function Replace-ExactlyOnce([string] $source, [string] $needle, [string] $replacement) {
    $first = $source.IndexOf($needle, [StringComparison]::Ordinal)
    $last = $source.LastIndexOf($needle, [StringComparison]::Ordinal)
    if ($first -lt 0 -or $first -ne $last) {
        throw "Expected exactly one occurrence of: $needle"
    }
    return $source.Substring(0, $first) + $replacement + $source.Substring($first + $needle.Length)
}

$path = 'MacroEngine/ui/AppController.cs'
$text = [IO.File]::ReadAllText($path)

$text = Replace-ExactlyOnce $text @'
    private SettingsWindow? _settingsWindow;
    private bool _isRunning;
'@ @'
    private SettingsWindow? _settingsWindow;
    private HelpWindow? _helpWindow;
    private AboutWindow? _aboutWindow;
    private bool _isRunning;
'@

$text = Replace-ExactlyOnce $text @'
        var settingsItem = new NativeMenuItem("Настройки…");
        settingsItem.Click += (_, _) => OpenSettings();

        _cancelItem = new NativeMenuItem("Остановить текущее действие") { IsEnabled = false };
'@ @'
        var settingsItem = new NativeMenuItem("Настройки…");
        settingsItem.Click += (_, _) => OpenSettings();

        var helpItem = new NativeMenuItem("Справка…");
        helpItem.Click += (_, _) => OpenHelp();

        var dataFolderItem = new NativeMenuItem("Открыть папку данных");
        dataFolderItem.Click += (_, _) => OpenDataFolder();

        var aboutItem = new NativeMenuItem("О программе…");
        aboutItem.Click += (_, _) => OpenAbout();

        _cancelItem = new NativeMenuItem("Остановить текущее действие") { IsEnabled = false };
'@

$text = Replace-ExactlyOnce $text @'
                    reloadItem,
                    settingsItem,
                    new NativeMenuItemSeparator(),
                    _autostartItem,
'@ @'
                    reloadItem,
                    settingsItem,
                    helpItem,
                    dataFolderItem,
                    aboutItem,
                    new NativeMenuItemSeparator(),
                    _autostartItem,
'@

$text = Replace-ExactlyOnce $text '            ToolTipText = "MacroEngine — остановлен",' '            ToolTipText = $"MacroEngine {ProductInfo.DisplayVersion} — остановлен",'

$text = Replace-ExactlyOnce $text @'
        if (!File.Exists(AppPaths.FirstRunMarker))
        {
            try { File.WriteAllText(AppPaths.FirstRunMarker, ""); } catch { }
            _overlay.ShowToast("MacroEngine запущен — правый клик по иконке в трее", 5000);
        }
'@ @'
        if (!File.Exists(AppPaths.FirstRunMarker))
        {
            try { File.WriteAllText(AppPaths.FirstRunMarker, ProductInfo.Version); } catch { }
            _overlay.ShowToast($"MacroEngine {ProductInfo.DisplayVersion} запущен — справка открыта", 5000);
            Avalonia.Threading.Dispatcher.UIThread.Post(OpenHelp);
        }
'@

$text = Replace-ExactlyOnce $text '        _trayIcon.ToolTipText = $"MacroEngine — {status}";' '        _trayIcon.ToolTipText = $"MacroEngine {ProductInfo.DisplayVersion} — {status}";'

$text = Replace-ExactlyOnce $text @'
    private void OpenSettings()
    {
'@ @'
    private void OpenHelp()
    {
        if (_helpWindow != null)
        {
            _helpWindow.Activate();
            return;
        }

        _helpWindow = new HelpWindow(OpenSettings, OpenAbout);
        _helpWindow.Closed += (_, _) => _helpWindow = null;
        _helpWindow.Show();
        _helpWindow.Activate();
    }

    private void OpenAbout()
    {
        if (_aboutWindow != null)
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new AboutWindow(OpenHelp);
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Show();
        _aboutWindow.Activate();
    }

    private void OpenDataFolder()
    {
        try
        {
            ShellTools.OpenDirectory(AppPaths.RootDirectory);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Open data directory failed: {ex.GetType().Name}: {ex.Message}");
            _overlay.ShowToast($"Не удалось открыть папку данных: {ex.Message}", 5000);
        }
    }

    private void OpenSettings()
    {
'@

$text = Replace-ExactlyOnce $text @'
        _trayIcon.Dispose();
        _overlay.Close();
        _config.Dispose();
'@ @'
        _trayIcon.Dispose();
        _helpWindow?.Close();
        _aboutWindow?.Close();
        _overlay.Close();
        _config.Dispose();
'@

[IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
