$ErrorActionPreference = 'Stop'

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
