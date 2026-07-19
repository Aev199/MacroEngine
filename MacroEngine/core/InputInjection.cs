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
}
