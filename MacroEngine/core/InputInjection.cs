using System.Runtime.InteropServices;

namespace MacroEngine.Core;

/// <summary>Checked wrappers around Windows input injection APIs.</summary>
internal static class InputInjection
{
    public static void Send(IReadOnlyList<NativeMethods.INPUT> inputs)
    {
        if (inputs.Count == 0)
            return;

        NativeMethods.INPUT[] array = inputs as NativeMethods.INPUT[] ?? inputs.ToArray();
        uint sent = NativeMethods.SendInput(
            (uint)array.Length,
            array,
            Marshal.SizeOf<NativeMethods.INPUT>());

        if (sent != (uint)array.Length)
        {
            TryReleasePartialDownStates(array, (int)sent);
            throw new InvalidOperationException(
                "Windows заблокировала эмуляцию ввода. Возможно, целевое приложение запущено от администратора. " +
                $"Отправлено событий: {sent} из {array.Length}.");
        }
    }

    public static void SetCursorPosition(int x, int y)
    {
        if (!NativeMethods.SetCursorPos(x, y))
            throw new InvalidOperationException("Windows не разрешила переместить указатель мыши.");
    }

    private static void TryReleasePartialDownStates(NativeMethods.INPUT[] inputs, int sentCount)
    {
        if (sentCount <= 0) return;

        var releases = new List<NativeMethods.INPUT>();
        for (int index = Math.Min(sentCount, inputs.Length) - 1; index >= 0; index--)
        {
            NativeMethods.INPUT input = inputs[index];
            if (input.type == NativeMethods.INPUT_KEYBOARD
                && (input.ki.dwFlags & NativeMethods.KEYEVENTF_KEYUP) == 0)
            {
                input.ki.dwFlags |= NativeMethods.KEYEVENTF_KEYUP;
                releases.Add(input);
                continue;
            }

            if (input.type == NativeMethods.INPUT_MOUSE)
            {
                uint upFlags = 0;
                if ((input.mi.dwFlags & NativeMethods.MOUSEEVENTF_LEFTDOWN) != 0)
                    upFlags |= NativeMethods.MOUSEEVENTF_LEFTUP;
                if ((input.mi.dwFlags & NativeMethods.MOUSEEVENTF_RIGHTDOWN) != 0)
                    upFlags |= NativeMethods.MOUSEEVENTF_RIGHTUP;

                if (upFlags != 0)
                {
                    input.mi.dwFlags = upFlags;
                    releases.Add(input);
                }
            }
        }

        if (releases.Count == 0) return;
        try
        {
            NativeMethods.SendInput(
                (uint)releases.Count,
                releases.ToArray(),
                Marshal.SizeOf<NativeMethods.INPUT>());
        }
        catch
        {
            // The original error remains the actionable failure.
        }
    }
}
