namespace MacroEngine.Core;

/// <summary>
/// Identifies the exact window that owned the trigger. Automation is allowed to
/// send input only while this same HWND/PID pair remains in the foreground.
/// </summary>
internal readonly record struct AutomationTarget(IntPtr Handle, uint ProcessId)
{
    public bool IsCaptured => Handle != IntPtr.Zero && ProcessId != 0;

    public static AutomationTarget Capture()
    {
        IntPtr handle = NativeMethods.GetForegroundWindow();
        if (handle == IntPtr.Zero)
            return default;

        NativeMethods.GetWindowThreadProcessId(handle, out uint processId);
        return processId == 0 ? default : new AutomationTarget(handle, processId);
    }

    public void ThrowIfNotForeground(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsSameWindowAlive() || NativeMethods.GetForegroundWindow() != Handle)
        {
            throw new OperationCanceledException(
                "Активное окно изменилось — действие остановлено, чтобы не отправить ввод не туда.",
                cancellationToken);
        }
    }

    /// <summary>
    /// A MacroEngine prompt is the only intentional focus change. Restore the
    /// original target only when the current foreground window still belongs to
    /// MacroEngine; never steal focus back from another user-selected app.
    /// </summary>
    public void RestoreAfterPrompt(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsSameWindowAlive())
            throw new OperationCanceledException("Исходное окно было закрыто.", cancellationToken);

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == Handle)
            return;

        NativeMethods.GetWindowThreadProcessId(foreground, out uint foregroundProcessId);
        if (foregroundProcessId != (uint)Environment.ProcessId)
        {
            throw new OperationCanceledException(
                "Во время ввода пользователь переключился в другое приложение.",
                cancellationToken);
        }

        if (!NativeMethods.SetForegroundWindow(Handle))
        {
            throw new InvalidOperationException(
                "Windows не разрешила вернуть фокус исходному окну. Действие отменено.");
        }

        if (cancellationToken.WaitHandle.WaitOne(40))
            cancellationToken.ThrowIfCancellationRequested();

        ThrowIfNotForeground(cancellationToken);
    }

    private bool IsSameWindowAlive()
    {
        if (!IsCaptured || !NativeMethods.IsWindow(Handle))
            return false;

        NativeMethods.GetWindowThreadProcessId(Handle, out uint currentProcessId);
        return currentProcessId == ProcessId;
    }
}
