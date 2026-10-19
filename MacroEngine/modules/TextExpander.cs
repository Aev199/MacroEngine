using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MacroEngine.Core;
using MacroEngine.UI;

namespace MacroEngine.Modules;

/// <summary>
/// Handles text expansion, token resolution and checked input injection. Every
/// operation remains bound to the exact window that owned the trigger.
/// </summary>
internal static class TextExpander
{
    private const int VK_BACK = 0x08;
    private const int VK_RETURN = 0x0D;
    private const int VK_LEFT = 0x25;
    private const string CursorMarker = "{cursor}";

    public static void Expand(
        string rawReplacement,
        int triggerLength,
        AutomationTarget target,
        CancellationToken cancellationToken = default)
    {
        bool previousSuppression = KeyInterceptor.IsSuppressed;
        KeyInterceptor.IsSuppressed = true;
        try
        {
            target.ThrowIfNotForeground(cancellationToken);
            EraseChars(triggerLength, target, cancellationToken);

            string replacement = ResolveTokens(rawReplacement, target, cancellationToken);

            int caretBack = 0;
            int idx = replacement.IndexOf(CursorMarker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                caretBack = replacement.Length - (idx + CursorMarker.Length);
                replacement = replacement.Remove(idx, CursorMarker.Length);
            }

            TypeUnicode(replacement, target, cancellationToken);

            for (int i = 0; i < caretBack; i++)
            {
                target.ThrowIfNotForeground(cancellationToken);
                SendKeyDownUp(VK_LEFT);
            }
        }
        finally
        {
            KeyInterceptor.IsSuppressed = previousSuppression;
        }
    }

    public static void ExpandRichText(
        string rtfOrPath,
        int triggerLength,
        AutomationTarget target,
        CancellationToken cancellationToken = default)
    {
        bool previousSuppression = KeyInterceptor.IsSuppressed;
        KeyInterceptor.IsSuppressed = true;
        try
        {
            target.ThrowIfNotForeground(cancellationToken);
            EraseChars(triggerLength, target, cancellationToken);

            string rtf = rtfOrPath;
            if (File.Exists(rtfOrPath) && rtfOrPath.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase))
                rtf = File.ReadAllText(rtfOrPath, System.Text.Encoding.Default);

            string resolved = ResolveTokens(rtf, target, cancellationToken);
            PasteRichText(resolved, target, cancellationToken);
        }
        finally
        {
            KeyInterceptor.IsSuppressed = previousSuppression;
        }
    }

    public static void LoadLisp(
        string filePath,
        int triggerLength,
        AutomationTarget target,
        CancellationToken cancellationToken = default)
    {
        bool previousSuppression = KeyInterceptor.IsSuppressed;
        KeyInterceptor.IsSuppressed = true;
        try
        {
            target.ThrowIfNotForeground(cancellationToken);
            EraseChars(triggerLength, target, cancellationToken);

            string resolved = ResolveTokens(filePath, target, cancellationToken)
                .Replace('\\', '/')
                .Replace("\"", "\\\"");

            TypeUnicode($"(load \"{resolved}\")\n", target, cancellationToken);
        }
        finally
        {
            KeyInterceptor.IsSuppressed = previousSuppression;
        }
    }

    internal static string ResolveTokens(
        string text,
        AutomationTarget target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        text = Regex.Replace(text, @"\{datetime:([^}]*)\}", match =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return DateTime.Now.ToString(match.Groups[1].Value); }
            catch { return match.Value; }
        });

        text = text
            .Replace("{date}", DateTime.Now.ToString("dd.MM.yyyy"))
            .Replace("{time}", DateTime.Now.ToString("HH:mm"))
            .Replace("{datetime}", DateTime.Now.ToString("dd.MM.yyyy HH:mm"))
            .Replace("{year}", DateTime.Now.Year.ToString())
            .Replace("{clipboard}", ReadClipboardSafe());

        text = Regex.Replace(text, @"\{input(?::([^}]*))?\}", match =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            target.ThrowIfNotForeground(cancellationToken);
            string label = match.Groups[1].Success && match.Groups[1].Value.Length > 0
                ? match.Groups[1].Value
                : "Введите значение:";
            string value = PromptWindow.AskText(label, cancellationToken);
            target.RestoreAfterPrompt(cancellationToken);
            return value;
        });

        text = Regex.Replace(text, @"\{choice:([^}]*)\}", match =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            target.ThrowIfNotForeground(cancellationToken);
            var options = match.Groups[1].Value.Split('|',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string value = options.Length > 0
                ? PromptWindow.AskChoice("Выберите:", options, cancellationToken)
                : "";
            if (options.Length > 0)
                target.RestoreAfterPrompt(cancellationToken);
            return value;
        });

        target.ThrowIfNotForeground(cancellationToken);

        return text;
    }

    /// <summary>Erase characters while continuously checking cancellation and focus.</summary>
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
    public static void TypeText(
        string raw,
        AutomationTarget target,
        CancellationToken cancellationToken = default)
    {
        string text = ResolveTokens(raw, target, cancellationToken)
            .Replace(CursorMarker, "", StringComparison.OrdinalIgnoreCase);
        TypeUnicode(text, target, cancellationToken);
    }

    private static void Delay(int milliseconds, CancellationToken cancellationToken)
    {
        if (milliseconds <= 0) return;
        if (cancellationToken.WaitHandle.WaitOne(milliseconds))
            cancellationToken.ThrowIfCancellationRequested();
    }

    private static string ReadClipboardSafe()
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                return System.Windows.Forms.Clipboard.ContainsText()
                    ? System.Windows.Forms.Clipboard.GetText()
                    : "";
            }
            catch (Exception ex)
            {
                lastError = ex;
                Thread.Sleep(20);
            }
        }

        throw new InvalidOperationException(
            "Не удалось прочитать текст из буфера обмена.",
            lastError);
    }

    private static void TypeUnicode(
        string text,
        AutomationTarget target,
        CancellationToken cancellationToken)
    {
        var inputs = new List<NativeMethods.INPUT>(128);

        for (int i = 0; i < text.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            char c = text[i];

            if (c == '\n' || (c == '\\' && i + 1 < text.Length && text[i + 1] == 'n'))
            {
                if (c == '\\') i++;
                uint scan = NativeMethods.MapVirtualKey(VK_RETURN, NativeMethods.MAPVK_VK_TO_VSC);
                inputs.Add(MakeVkInput(VK_RETURN, scan, 0));
                inputs.Add(MakeVkInput(VK_RETURN, scan, NativeMethods.KEYEVENTF_KEYUP));
            }
            else
            {
                inputs.Add(MakeUnicodeInput(c, isUp: false));
                inputs.Add(MakeUnicodeInput(c, isUp: true));
            }

            if (inputs.Count >= 128)
                FlushInputs(inputs, target, cancellationToken);
        }

        FlushInputs(inputs, target, cancellationToken);
    }

    private static void FlushInputs(
        List<NativeMethods.INPUT> inputs,
        AutomationTarget target,
        CancellationToken cancellationToken)
    {
        if (inputs.Count == 0) return;
        target.ThrowIfNotForeground(cancellationToken);
        InputInjection.Send(inputs);
        inputs.Clear();
    }

    private static NativeMethods.INPUT MakeUnicodeInput(char c, bool isUp) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        ki = new NativeMethods.KEYBDINPUT
        {
            wVk = 0,
            wScan = c,
            dwFlags = NativeMethods.KEYEVENTF_UNICODE | (isUp ? NativeMethods.KEYEVENTF_KEYUP : 0u),
            time = 0,
            dwExtraInfo = IntPtr.Zero
        }
    };

    private static NativeMethods.INPUT MakeVkInput(int vk, uint scan, uint flags) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        ki = new NativeMethods.KEYBDINPUT
        {
            wVk = (ushort)vk,
            wScan = (ushort)scan,
            dwFlags = flags,
            time = 0,
            dwExtraInfo = IntPtr.Zero
        }
    };

    private static void SendKeyDownUp(int vkCode)
    {
        uint scanCode = NativeMethods.MapVirtualKey((uint)vkCode, NativeMethods.MAPVK_VK_TO_VSC);
        bool extended = InputInjection.RequiresExtendedKeyFlag((ushort)vkCode);
        uint flags = extended ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0u;

        InputInjection.Send(new[]
        {
            MakeVkInput(vkCode, scanCode, flags),
            MakeVkInput(vkCode, scanCode, flags | NativeMethods.KEYEVENTF_KEYUP)
        });
    }

    private static void PasteRichText(
        string rtf,
        AutomationTarget target,
        CancellationToken cancellationToken)
    {
        target.ThrowIfNotForeground(cancellationToken);
        ClipboardSnapshot snapshot = ClipboardSnapshot.Capture();
        uint replacementSequence = 0;
        bool restoreSnapshot = true;

        try
        {
            if (!snapshot.IsCurrent)
            {
                restoreSnapshot = false;
                throw new InvalidOperationException(
                    "Буфер обмена изменился во время подготовки вставки. Операция отменена.");
            }

            string plain = StripRtf(rtf);
            AppLog.Diagnostic($"RTF paste prepared: rtf={rtf.Length}; plain={plain.Length}");

            bool written = false;
            try
            {
                var dataObject = new System.Windows.Forms.DataObject();
                dataObject.SetData(System.Windows.Forms.DataFormats.Rtf, rtf);
                dataObject.SetData(System.Windows.Forms.DataFormats.Text, plain);
                System.Windows.Forms.Clipboard.SetDataObject(dataObject, true, 10, 50);
                written = true;
            }
            catch (Exception ex)
            {
                AppLog.Diagnostic($"RTF clipboard .NET path failed: {ex.GetType().Name}: {ex.Message}");
            }

            if (!written)
                written = TryWriteRtfWithWinApi(rtf, plain);

            if (!written)
                throw new InvalidOperationException("Не удалось подготовить форматированный текст в буфере обмена.");

            replacementSequence = NativeMethods.GetClipboardSequenceNumber();

            Delay(50, cancellationToken);
            target.ThrowIfNotForeground(cancellationToken);
            if (NativeMethods.GetClipboardSequenceNumber() != replacementSequence)
            {
                restoreSnapshot = false;
                throw new InvalidOperationException(
                    "Буфер обмена изменился перед вставкой. Операция отменена.");
            }
            SendCtrlV();
            Delay(150, cancellationToken);
        }
        finally
        {
            if (restoreSnapshot
                && (replacementSequence == 0
                    || NativeMethods.GetClipboardSequenceNumber() == replacementSequence))
            {
                snapshot.Restore();
            }
            else if (replacementSequence != 0)
            {
                AppLog.Write("Clipboard changed during RTF paste; previous snapshot was not restored");
            }
        }
    }

    private static bool TryWriteRtfWithWinApi(string rtf, string plain)
    {
        uint cfRtf = NativeMethods.RegisterClipboardFormat("Rich Text Format");
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (!NativeMethods.OpenClipboard(IntPtr.Zero))
            {
                Thread.Sleep(3);
                continue;
            }

            try
            {
                if (!NativeMethods.EmptyClipboard())
                    return false;

                IntPtr hRtf = AllocString(rtf, asAnsi: true);
                IntPtr hText = AllocString(plain, asAnsi: false);
                if (hRtf == IntPtr.Zero || hText == IntPtr.Zero)
                {
                    FreeClipboardHandle(hRtf);
                    FreeClipboardHandle(hText);
                    return false;
                }

                if (NativeMethods.SetClipboardData(cfRtf, hRtf) == IntPtr.Zero)
                {
                    FreeClipboardHandle(hRtf);
                    FreeClipboardHandle(hText);
                    return false;
                }

                // SetClipboardData transferred ownership of hRtf to Windows.
                if (NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, hText) == IntPtr.Zero)
                {
                    FreeClipboardHandle(hText);
                    return false;
                }

                return true;
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }

        return false;
    }

    private static IntPtr AllocString(string text, bool asAnsi)
    {
        if (string.IsNullOrEmpty(text)) return IntPtr.Zero;
        byte[] bytes = asAnsi
            ? System.Text.Encoding.Default.GetBytes(text)
            : System.Text.Encoding.Unicode.GetBytes(text);
        int size = bytes.Length + (asAnsi ? 1 : 2);
        // GMEM_MOVEABLE is required by SetClipboardData; ZEROINIT guarantees
        // the extra byte(s) form the terminating NUL for ANSI/Unicode strings.
        IntPtr memory = NativeMethods.GlobalAlloc(0x0042, (UIntPtr)size);
        if (memory == IntPtr.Zero) return IntPtr.Zero;
        IntPtr pointer = NativeMethods.GlobalLock(memory);
        if (pointer == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(memory);
            return IntPtr.Zero;
        }

        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        NativeMethods.GlobalUnlock(memory);
        return memory;
    }

    private static void FreeClipboardHandle(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            NativeMethods.GlobalFree(handle);
    }

    private static string StripRtf(string rtf)
    {
        var builder = new System.Text.StringBuilder();
        bool inTag = false;
        int groupDepth = 0;
        for (int i = 0; i < rtf.Length; i++)
        {
            char c = rtf[i];
            if (c == '{') { groupDepth++; continue; }
            if (c == '}') { groupDepth--; continue; }
            if (c == '\\' && i + 1 < rtf.Length && char.IsLetter(rtf[i + 1]))
            {
                inTag = true;
                continue;
            }
            if (inTag)
            {
                if (c == ' ') inTag = false;
                continue;
            }
            if (c == '\\' && i + 1 < rtf.Length && "\\{}".Contains(rtf[i + 1]))
            {
                builder.Append(rtf[++i]);
                continue;
            }
            if (c == '\\' && i + 1 < rtf.Length && rtf[i + 1] == 'n')
            {
                builder.Append('\n');
                i++;
                continue;
            }
            if (groupDepth > 0 && !inTag) builder.Append(c);
        }
        return builder.ToString().Trim();
    }

    private static void SendCtrlV()
    {
        const int VK_CTRL = 0x11;
        const int VK_V = 0x56;
        uint ctrlScan = NativeMethods.MapVirtualKey(VK_CTRL, NativeMethods.MAPVK_VK_TO_VSC);
        uint vScan = NativeMethods.MapVirtualKey(VK_V, NativeMethods.MAPVK_VK_TO_VSC);
        InputInjection.Send(new[]
        {
            MakeVkInput(VK_CTRL, ctrlScan, 0),
            MakeVkInput(VK_V, vScan, 0),
            MakeVkInput(VK_V, vScan, NativeMethods.KEYEVENTF_KEYUP),
            MakeVkInput(VK_CTRL, ctrlScan, NativeMethods.KEYEVENTF_KEYUP)
        });
    }
}
