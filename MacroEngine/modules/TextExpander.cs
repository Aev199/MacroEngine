using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MacroEngine.Core;
using MacroEngine.UI;

namespace MacroEngine.Modules;

/// <summary>
/// Handles text expansion: erases trigger, resolves tokens, types replacement.
/// Uses Unicode keystroke injection — 100% reliable, no clipboard, no Ctrl+V.
/// </summary>
internal static class TextExpander
{
    private const int VK_BACK = 0x08;
    private const int VK_RETURN = 0x0D;
    private const int VK_LEFT = 0x25;
    private const string CursorMarker = "{cursor}";

    /// <summary>Perform text expansion.</summary>
    public static void Expand(string rawReplacement, int triggerLength)
    {
        KeyInterceptor.IsSuppressed = true;

        // Capture the target window up front so we can restore focus if a
        // {input}/{choice} prompt steals it during token resolution.
        IntPtr target = NativeMethods.GetForegroundWindow();
        bool mayPrompt = MayPrompt(rawReplacement);

        try
        {
            Thread.Sleep(60);
            for (int i = 0; i < triggerLength; i++)
            {
                SendKeyDownUp(VK_BACK);
                Thread.Sleep(15);
            }
            Thread.Sleep(30);

            string replacement = ResolveTokens(rawReplacement);

            if (mayPrompt && target != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(target);
                Thread.Sleep(50);
            }

            // {cursor} marks where the caret should land — type everything, then
            // walk the caret back over the characters that follow the marker.
            int caretBack = 0;
            int idx = replacement.IndexOf(CursorMarker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                caretBack = replacement.Length - (idx + CursorMarker.Length);
                replacement = replacement.Remove(idx, CursorMarker.Length);
            }

            TypeUnicode(replacement);

            for (int i = 0; i < caretBack; i++)
                SendKeyDownUp(VK_LEFT);
        }
        finally { KeyInterceptor.IsSuppressed = false; }
    }

    /// <summary>Perform richtext expansion — RTF clipboard + Ctrl+V for Word.</summary>
    public static void ExpandRichText(string rtfOrPath, int triggerLength)
    {
        KeyInterceptor.IsSuppressed = true;
        try
        {
            Thread.Sleep(60);
            for (int i = 0; i < triggerLength; i++)
            {
                SendKeyDownUp(VK_BACK);
                Thread.Sleep(15);
            }
            Thread.Sleep(30);

            // If trigger value is a path to .rtf file → load it
            string rtf = rtfOrPath;
            if (File.Exists(rtfOrPath) && rtfOrPath.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase))
                rtf = File.ReadAllText(rtfOrPath, System.Text.Encoding.Default);

            string resolved = ResolveTokens(rtf);
            PasteRichText(resolved);
        }
        finally { KeyInterceptor.IsSuppressed = false; }
    }

    /// <summary>
    /// Load a LISP file into AutoCAD by typing (load "path") into the command line.
    /// Expects a file path as trigger value (may contain {tokens}).
    /// </summary>
    public static void LoadLisp(string filePath, int triggerLength)
    {
        KeyInterceptor.IsSuppressed = true;
        try
        {
            Thread.Sleep(60);
            for (int i = 0; i < triggerLength; i++)
            {
                SendKeyDownUp(VK_BACK);
                Thread.Sleep(15);
            }
            Thread.Sleep(30);

            string resolved = ResolveTokens(filePath);
            // Normalize to forward slashes — AutoCAD LISP accepts both, but forward avoids escape issues
            resolved = resolved.Replace('\\', '/');

            // Escape double-quotes inside path (paranoid, but safe)
            resolved = resolved.Replace("\"", "\\\"");

            string cmd = $"(load \"{resolved}\")\n";
            TypeUnicode(cmd);
        }
        finally { KeyInterceptor.IsSuppressed = false; }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Dynamic tokens: {date} {time} {clipboard} {year} {datetime}
    // ═══════════════════════════════════════════════════════════════

    private static string ResolveTokens(string text)
    {
        // Custom date/time format: {datetime:HH:mm} or {datetime:yyyy-MM-dd}
        text = Regex.Replace(text, @"\{datetime:([^}]*)\}", m =>
        {
            try { return DateTime.Now.ToString(m.Groups[1].Value); }
            catch { return m.Value; }
        });

        text = text
            .Replace("{date}", DateTime.Now.ToString("dd.MM.yyyy"))
            .Replace("{time}", DateTime.Now.ToString("HH:mm"))
            .Replace("{datetime}", DateTime.Now.ToString("dd.MM.yyyy HH:mm"))
            .Replace("{year}", DateTime.Now.Year.ToString())
            .Replace("{clipboard}", ReadClipboardSafe());

        // {input} or {input:Подпись} — prompt the user for free text.
        text = Regex.Replace(text, @"\{input(?::([^}]*))?\}", m =>
        {
            string label = m.Groups[1].Success && m.Groups[1].Value.Length > 0
                ? m.Groups[1].Value
                : "Введите значение:";
            return PromptForm.AskText(label);
        });

        // {choice:a|b|c} — let the user pick one option.
        text = Regex.Replace(text, @"\{choice:([^}]*)\}", m =>
        {
            var opts = m.Groups[1].Value.Split('|',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return opts.Length > 0 ? PromptForm.AskChoice("Выберите:", opts) : "";
        });

        return text;
    }

    /// <summary>
    /// Resolve tokens and type the text, without erasing anything beforehand.
    /// Used by macro "type" steps. The caller manages <see cref="KeyInterceptor.IsSuppressed"/>.
    /// </summary>
    public static void TypeText(string raw)
    {
        string s = ResolveTokens(raw).Replace(CursorMarker, "", StringComparison.OrdinalIgnoreCase);
        TypeUnicode(s);
    }

    /// <summary>True if resolving the text will pop a modal prompt that can steal focus.</summary>
    private static bool MayPrompt(string text) =>
        text.Contains("{input", StringComparison.OrdinalIgnoreCase)
        || text.Contains("{choice", StringComparison.OrdinalIgnoreCase);

    private static string ReadClipboardSafe()
    {
        try
        {
            return System.Windows.Forms.Clipboard.ContainsText()
                ? System.Windows.Forms.Clipboard.GetText()
                : "";
        }
        catch { return ""; }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Unicode keystroke injection
    // ═══════════════════════════════════════════════════════════════

    private static void TypeUnicode(string text)
    {
        var inputs = new List<NativeMethods.INPUT>(text.Length * 2);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            // Real newline OR literal \n → Enter key
            if (c == '\n' || (c == '\\' && i + 1 < text.Length && text[i + 1] == 'n'))
            {
                if (c == '\\') i++; // skip 'n'
                uint scan = NativeMethods.MapVirtualKey(VK_RETURN, NativeMethods.MAPVK_VK_TO_VSC);
                inputs.Add(MakeVkInput(VK_RETURN, scan, 0));
                inputs.Add(MakeVkInput(VK_RETURN, scan, NativeMethods.KEYEVENTF_KEYUP));
            }
            else
            {
                inputs.Add(MakeUnicodeInput(c, isUp: false));
                inputs.Add(MakeUnicodeInput(c, isUp: true));
            }
        }

        if (inputs.Count > 0)
            NativeMethods.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT MakeUnicodeInput(char c, bool isUp)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = 0,
                wScan = (ushort)c,
                dwFlags = NativeMethods.KEYEVENTF_UNICODE | (isUp ? NativeMethods.KEYEVENTF_KEYUP : 0u),
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };
    }

    private static NativeMethods.INPUT MakeVkInput(int vk, uint scan, uint flags)
    {
        return new NativeMethods.INPUT
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
    }

    // ═══════════════════════════════════════════════════════════════
    //  Shared: virtual key down+up
    // ═══════════════════════════════════════════════════════════════

    private static void SendKeyDownUp(int vkCode)
    {
        uint scanCode = NativeMethods.MapVirtualKey((uint)vkCode, NativeMethods.MAPVK_VK_TO_VSC);
        // Arrow keys (0x25–0x28) are extended keys; keep the existing VK_BACK flag too.
        bool extended = vkCode == VK_BACK || (vkCode >= 0x25 && vkCode <= 0x28);
        uint flags = extended ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0u;

        var inputs = new[]
        {
            new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD, ki = new NativeMethods.KEYBDINPUT { wVk = (ushort)vkCode, wScan = (ushort)scanCode, dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero } },
            new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD, ki = new NativeMethods.KEYBDINPUT { wVk = (ushort)vkCode, wScan = (ushort)scanCode, dwFlags = flags | NativeMethods.KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero } }
        };

        NativeMethods.SendInput(2, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    // ═══════════════════════════════════════════════════════════════
    //  Rich text paste (for Word, Outlook — formatted templates)
    // ═══════════════════════════════════════════════════════════════

    private static void PasteRichText(string rtf)
    {
        string plain = StripRtf(rtf);
        LogToFile($"[RTF] len={rtf.Length} plain={plain.Length}");

        string? saved = null;
        try { if (System.Windows.Forms.Clipboard.ContainsText()) saved = System.Windows.Forms.Clipboard.GetText(); }
        catch { }

        // Try .NET clipboard with retry
        bool written = false;
        try
        {
            var dataObj = new System.Windows.Forms.DataObject();
            dataObj.SetData(System.Windows.Forms.DataFormats.Rtf, rtf);
            dataObj.SetData(System.Windows.Forms.DataFormats.Text, plain);
            System.Windows.Forms.Clipboard.SetDataObject(dataObj, true, 10, 50);
            written = true;
            LogToFile("[RTF] .NET clipboard OK");
        }
        catch (Exception ex) { LogToFile($"[RTF] .NET FAIL: {ex.Message}"); }

        if (!written)
        {
            uint cfRtf = NativeMethods.RegisterClipboardFormat("Rich Text Format");
            for (int a = 0; a < 10; a++)
            {
                if (NativeMethods.OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        NativeMethods.EmptyClipboard();
                        IntPtr hRtf = AllocString(rtf, true);
                        IntPtr hText = AllocString(plain, false);
                        if (hRtf != IntPtr.Zero) NativeMethods.SetClipboardData(cfRtf, hRtf);
                        if (hText != IntPtr.Zero) NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, hText);
                        written = true; LogToFile("[RTF] WinAPI OK");
                    }
                    finally { NativeMethods.CloseClipboard(); }
                    break;
                }
                Thread.Sleep(3);
            }
        }

        if (!written) { LogToFile("[RTF] ABORT"); return; }

        Thread.Sleep(50);
        SendCtrlV();
        LogToFile("[RTF] Ctrl+V sent");
        Thread.Sleep(150);

        if (saved != null) { try { System.Windows.Forms.Clipboard.SetText(saved); } catch { } }
    }

    private static IntPtr AllocString(string text, bool asAnsi)
    {
        if (string.IsNullOrEmpty(text)) return IntPtr.Zero;
        byte[] bytes = asAnsi
            ? System.Text.Encoding.Default.GetBytes(text)
            : System.Text.Encoding.Unicode.GetBytes(text);
        int size = bytes.Length + (asAnsi ? 1 : 2); // null terminator
        IntPtr hMem = NativeMethods.GlobalAlloc(0x0002, (UIntPtr)size);
        if (hMem == IntPtr.Zero) return IntPtr.Zero;
        IntPtr ptr = NativeMethods.GlobalLock(hMem);
        if (ptr != IntPtr.Zero) { Marshal.Copy(bytes, 0, ptr, bytes.Length); NativeMethods.GlobalUnlock(hMem); }
        return hMem;
    }

    private static void WriteClipboardRtf(string rtf, string plainText)
    {
        uint cfRtf = NativeMethods.RegisterClipboardFormat("Rich Text Format");

        if (!NativeMethods.OpenClipboard(IntPtr.Zero))
        {
            LogToFile("[RichText] OpenClipboard failed");
            return;
        }

        try
        {
            NativeMethods.EmptyClipboard();

            // RTF is always 8-bit ANSI (not UTF-16!)
            IntPtr hRtf = AllocAndWriteAnsi(rtf);
            if (hRtf != IntPtr.Zero)
                NativeMethods.SetClipboardData(cfRtf, hRtf);

            // Plain text fallback (UTF-16)
            IntPtr hText = AllocAndWriteUnicode(plainText);
            if (hText != IntPtr.Zero)
                NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, hText);
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static IntPtr AllocAndWriteAnsi(string text)
    {
        if (string.IsNullOrEmpty(text)) return IntPtr.Zero;
        byte[] bytes = System.Text.Encoding.Default.GetBytes(text);
        int size = bytes.Length + 1; // + null terminator
        IntPtr hMem = NativeMethods.GlobalAlloc(0x0002, (UIntPtr)size);
        if (hMem == IntPtr.Zero) return IntPtr.Zero;

        IntPtr ptr = NativeMethods.GlobalLock(hMem);
        if (ptr != IntPtr.Zero)
        {
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            Marshal.WriteByte(ptr, bytes.Length, 0); // null terminator
            NativeMethods.GlobalUnlock(hMem);
        }
        return hMem;
    }

    private static IntPtr AllocAndWriteUnicode(string text)
    {
        if (string.IsNullOrEmpty(text)) return IntPtr.Zero;
        int bytes = (text.Length + 1) * 2;
        IntPtr hMem = NativeMethods.GlobalAlloc(0x0002, (UIntPtr)bytes);
        if (hMem == IntPtr.Zero) return IntPtr.Zero;

        IntPtr ptr = NativeMethods.GlobalLock(hMem);
        if (ptr != IntPtr.Zero)
        {
            Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
            Marshal.WriteInt16(ptr, text.Length * 2, 0);
            NativeMethods.GlobalUnlock(hMem);
        }
        return hMem;
    }

    /// <summary>Strip RTF tags, return plain text.</summary>
    private static string StripRtf(string rtf)
    {
        // Remove RTF control words and groups
        var sb = new System.Text.StringBuilder();
        bool inTag = false;
        int groupDepth = 0;
        for (int i = 0; i < rtf.Length; i++)
        {
            char c = rtf[i];
            if (c == '{') { groupDepth++; continue; }
            if (c == '}')
            {
                groupDepth--;
                continue;
            }
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
                sb.Append(rtf[++i]);
                continue;
            }
            if (c == '\\' && i + 1 < rtf.Length && rtf[i + 1] == 'n')
            {
                sb.Append('\n'); i++;
                continue;
            }
            if (groupDepth > 0 && !inTag) sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    private static void SendCtrlV()
    {
        const int VK_CTRL = 0x11, VK_V = 0x56;
        uint cs = NativeMethods.MapVirtualKey(VK_CTRL, NativeMethods.MAPVK_VK_TO_VSC);
        uint vs = NativeMethods.MapVirtualKey(VK_V, NativeMethods.MAPVK_VK_TO_VSC);
        var inputs = new[]
        {
            new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD, ki = new NativeMethods.KEYBDINPUT { wVk = VK_CTRL, wScan = (ushort)cs, dwFlags = 0, time = 0, dwExtraInfo = IntPtr.Zero } },
            new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD, ki = new NativeMethods.KEYBDINPUT { wVk = VK_V, wScan = (ushort)vs, dwFlags = 0, time = 0, dwExtraInfo = IntPtr.Zero } },
            new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD, ki = new NativeMethods.KEYBDINPUT { wVk = VK_V, wScan = (ushort)vs, dwFlags = NativeMethods.KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero } },
            new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD, ki = new NativeMethods.KEYBDINPUT { wVk = VK_CTRL, wScan = (ushort)cs, dwFlags = NativeMethods.KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero } },
        };
        NativeMethods.SendInput(4, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void LogToFile(string message)
    {
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "macroengine.log");
            string line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch { /* never crash from logging */ }
    }
}
