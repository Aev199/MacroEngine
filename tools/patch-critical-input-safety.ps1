$ErrorActionPreference = 'Stop'

function Replace-ExactlyOnce([string] $source, [string] $needle, [string] $replacement) {
    $first = $source.IndexOf($needle, [StringComparison]::Ordinal)
    $last = $source.LastIndexOf($needle, [StringComparison]::Ordinal)
    if ($first -lt 0 -or $first -ne $last) {
        throw "Expected exactly one occurrence of: $needle"
    }
    return $source.Substring(0, $first) + $replacement + $source.Substring($first + $needle.Length)
}

$textPath = 'MacroEngine/modules/TextExpander.cs'
$text = [IO.File]::ReadAllText($textPath)
$erasePattern = '(?s)    public static void EraseChars\(.*?(?=    public static void TypeText\()'
$eraseReplacement = @'
    public static void EraseChars(
        int count,
        AutomationTarget target,
        CancellationToken cancellationToken = default)
    {
        bool previousSuppression = KeyInterceptor.IsSuppressed;
        KeyInterceptor.IsSuppressed = true;
        try
        {
            if (count <= 0)
            {
                target.ThrowIfNotForeground(cancellationToken);
                return;
            }

            Delay(60, cancellationToken);
            for (int i = 0; i < count; i++)
            {
                target.ThrowIfNotForeground(cancellationToken);
                SendKeyDownUp(VK_BACK);
                Delay(15, cancellationToken);
            }
            Delay(30, cancellationToken);
        }
        finally
        {
            KeyInterceptor.IsSuppressed = previousSuppression;
        }
    }

'@
$updated = [regex]::Replace($text, $erasePattern, $eraseReplacement, 1)
if ($updated -eq $text) { throw 'EraseChars replacement failed' }
[IO.File]::WriteAllText($textPath, $updated, [Text.UTF8Encoding]::new($false))

$queuePath = 'MacroEngine/core/ExecutionQueue.cs'
$queue = [IO.File]::ReadAllText($queuePath)
$queue = Replace-ExactlyOnce $queue `
    '    public event Action<string>? JobCancelled;' `
    '    public event Action<string, string?>? JobCancelled;'
$queue = Replace-ExactlyOnce $queue `
@'
            catch (OperationCanceledException)
            {
                JobCancelled?.Invoke(job.Description);
            }
'@.TrimEnd("`r", "`n") `
@'
            catch (OperationCanceledException ex)
            {
                JobCancelled?.Invoke(job.Description, ex.Message);
            }
'@.TrimEnd("`r", "`n")
[IO.File]::WriteAllText($queuePath, $queue, [Text.UTF8Encoding]::new($false))

$controllerPath = 'MacroEngine/ui/AppController.cs'
$controller = [IO.File]::ReadAllText($controllerPath)
$controller = Replace-ExactlyOnce $controller `
@'
        _executionQueue.JobCancelled += _ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _cancelItem.IsEnabled = false;
                UpdateUI();
                _overlay.ShowToast("Текущее действие остановлено");
            });
'@.TrimEnd("`r", "`n") `
@'
        _executionQueue.JobCancelled += (_, reason) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _cancelItem.IsEnabled = false;
                UpdateUI();
                _overlay.ShowToast(
                    string.IsNullOrWhiteSpace(reason) ? "Текущее действие остановлено" : reason,
                    5000);
            });
'@.TrimEnd("`r", "`n")
[IO.File]::WriteAllText($controllerPath, $controller, [Text.UTF8Encoding]::new($false))

$testPath = 'MacroEngine.Tests/ExecutionQueueTests.cs'
$tests = [IO.File]::ReadAllText($testPath)
$tests = Replace-ExactlyOnce $tests `
    '        queue.JobCancelled += _ => cancelled.Set();' `
    '        queue.JobCancelled += (_, _) => cancelled.Set();'
[IO.File]::WriteAllText($testPath, $tests, [Text.UTF8Encoding]::new($false))
