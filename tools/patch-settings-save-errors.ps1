$ErrorActionPreference = 'Stop'

$path = 'MacroEngine/ui/SettingsWindow.cs'
$text = [System.IO.File]::ReadAllText($path)

$triggerNeedle = '        _config.Save(entries);'
$triggerReplacement = @'
        try
        {
            _config.Save(entries);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Trigger config save failed: {ex.GetType().Name}: {ex.Message}");
            await MessageDialog.Show(this, "Ошибка сохранения",
                "Не удалось сохранить триггеры.\n\n" + ex.Message);
            return false;
        }
'@.TrimEnd("`r", "`n")

$macroNeedle = '        _macros.Save(_macroDefs);'
$macroReplacement = @'
        try
        {
            _macros.Save(_macroDefs);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Macro library save failed: {ex.GetType().Name}: {ex.Message}");
            await MessageDialog.Show(this, "Ошибка сохранения",
                "Не удалось сохранить макросы.\n\n" + ex.Message);
            return false;
        }
'@.TrimEnd("`r", "`n")

function Replace-ExactlyOnce([string] $source, [string] $needle, [string] $replacement) {
    $first = $source.IndexOf($needle, [StringComparison]::Ordinal)
    $last = $source.LastIndexOf($needle, [StringComparison]::Ordinal)
    if ($first -lt 0 -or $first -ne $last) {
        throw "Expected exactly one occurrence of: $needle"
    }
    return $source.Substring(0, $first) + $replacement + $source.Substring($first + $needle.Length)
}

$text = Replace-ExactlyOnce $text $triggerNeedle $triggerReplacement
$text = Replace-ExactlyOnce $text $macroNeedle $macroReplacement

[System.IO.File]::WriteAllText(
    $path,
    $text,
    [System.Text.UTF8Encoding]::new($false))
