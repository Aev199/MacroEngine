$ErrorActionPreference = 'Stop'

$path = 'MacroEngine/ui/AppController.cs'
$text = [System.IO.File]::ReadAllText($path)

function Replace-ExactlyOnce([string] $source, [string] $needle, [string] $replacement) {
    $first = $source.IndexOf($needle, [StringComparison]::Ordinal)
    $last = $source.LastIndexOf($needle, [StringComparison]::Ordinal)
    if ($first -lt 0 -or $first -ne $last) {
        throw "Expected exactly one occurrence of: $needle"
    }
    return $source.Substring(0, $first) + $replacement + $source.Substring($first + $needle.Length)
}

$text = Replace-ExactlyOnce $text `
    '    private readonly NativeMenuItem _autostartItem;' `
    "    private readonly NativeMenuItem _autostartItem;`n    private readonly NativeMenuItem _diagnosticItem;"

$autostartNeedle = @'
        _autostartItem.Click += (_, _) => ToggleAutostart();

        var quitItem = new NativeMenuItem("Выход");
'@.TrimEnd("`r", "`n")
$autostartReplacement = @'
        _autostartItem.Click += (_, _) => ToggleAutostart();

        _diagnosticItem = new NativeMenuItem("Диагностическое логирование")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = AppLog.DiagnosticEnabled
        };
        _diagnosticItem.Click += (_, _) => ToggleDiagnosticLogging();

        var quitItem = new NativeMenuItem("Выход");
'@.TrimEnd("`r", "`n")
$text = Replace-ExactlyOnce $text $autostartNeedle $autostartReplacement

$text = Replace-ExactlyOnce $text `
    '                    _autostartItem,' `
    "                    _autostartItem,`n                    _diagnosticItem,"

$methodNeedle = '    private void OpenSettings()'
$methodReplacement = @'
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
'@.TrimEnd("`r", "`n")
$text = Replace-ExactlyOnce $text $methodNeedle $methodReplacement

[System.IO.File]::WriteAllText(
    $path,
    $text,
    [System.Text.UTF8Encoding]::new($false))
