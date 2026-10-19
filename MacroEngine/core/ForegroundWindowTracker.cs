namespace MacroEngine.Core;

/// <summary>
/// Tracks the exact foreground HWND so a partially typed trigger can never span
/// two different windows, including windows owned by the same process.
/// </summary>
internal sealed class ForegroundWindowTracker
{
    private IntPtr _lastHandle;

    public void Reset(IntPtr handle) => _lastHandle = handle;

    public bool Update(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return false;

        bool changed = _lastHandle != IntPtr.Zero && _lastHandle != handle;
        _lastHandle = handle;
        return changed;
    }
}
