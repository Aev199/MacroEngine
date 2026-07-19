$ErrorActionPreference = 'Stop'

function Replace-ExactlyOnce([string] $source, [string] $needle, [string] $replacement) {
    $first = $source.IndexOf($needle, [StringComparison]::Ordinal)
    $last = $source.LastIndexOf($needle, [StringComparison]::Ordinal)
    if ($first -lt 0 -or $first -ne $last) {
        throw "Expected exactly one occurrence of: $needle"
    }
    return $source.Substring(0, $first) + $replacement + $source.Substring($first + $needle.Length)
}

$nativePath = 'MacroEngine/core/NativeMethods.cs'
$native = [IO.File]::ReadAllText($nativePath)
$nativeNeedle = @'
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
'@.TrimEnd("`r", "`n")
$nativeReplacement = @'
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
'@.TrimEnd("`r", "`n")
$native = Replace-ExactlyOnce $native $nativeNeedle $nativeReplacement
[IO.File]::WriteAllText($nativePath, $native, [Text.UTF8Encoding]::new($false))

$controllerPath = 'MacroEngine/ui/AppController.cs'
$controller = [IO.File]::ReadAllText($controllerPath)

$triggerPattern = '(?s)    private void OnTriggerMatched\(TriggerEntry entry\).*?(?=    private void ExecuteAction\()'
$triggerReplacement = @'
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
            target.ThrowIfNotForeground(cancellationToken);
            ExecuteAction(entry, action, eraseLength, target, cancellationToken);
        });

        if (!accepted)
        {
            AppLog.Write("Execution worker is busy; action rejected");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                _overlay.ShowToast("MacroEngine занят — новый запуск пропущен", 4000));
        }
    }

'@
$updated = [regex]::Replace($controller, $triggerPattern, $triggerReplacement, 1)
if ($updated -eq $controller) { throw 'OnTriggerMatched replacement failed' }
$controller = $updated

$executePattern = '(?s)    private void ExecuteAction\(.*?(?=    private static void OpenPath\()'
$executeReplacement = @'
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

'@
$updated = [regex]::Replace($controller, $executePattern, $executeReplacement, 1)
if ($updated -eq $controller) { throw 'ExecuteAction replacement failed' }
$controller = $updated

$launchPattern = '(?s)    private static void OpenPath\(.*?(?=    private void OnConfigChanged\()'
$launchReplacement = @'
    private static void OpenPath(
        string rawPath,
        int eraseLength,
        AutomationTarget target,
        CancellationToken cancellationToken)
    {
        KeyInterceptor.IsSuppressed = true;
        try
        {
            target.ThrowIfNotForeground(cancellationToken);
            TextExpander.EraseChars(eraseLength, target, cancellationToken);
            string path = TextExpander.ResolveTokens(rawPath, target, cancellationToken).Trim();
            cancellationToken.ThrowIfCancellationRequested();
            Process.Start("explorer.exe", path);
        }
        finally
        {
            KeyInterceptor.IsSuppressed = false;
        }
    }

    private static void Launch(
        string rawCommand,
        int eraseLength,
        AutomationTarget target,
        CancellationToken cancellationToken)
    {
        KeyInterceptor.IsSuppressed = true;
        try
        {
            target.ThrowIfNotForeground(cancellationToken);
            TextExpander.EraseChars(eraseLength, target, cancellationToken);
            string command = TextExpander.ResolveTokens(rawCommand, target, cancellationToken).Trim();
            cancellationToken.ThrowIfCancellationRequested();

            CommandLineParser.Split(command, out string fileName, out string arguments);
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true
            });
        }
        finally
        {
            KeyInterceptor.IsSuppressed = false;
        }
    }

'@
$updated = [regex]::Replace($controller, $launchPattern, $launchReplacement, 1)
if ($updated -eq $controller) { throw 'OpenPath/Launch replacement failed' }
$controller = $updated

[IO.File]::WriteAllText($controllerPath, $controller, [Text.UTF8Encoding]::new($false))
