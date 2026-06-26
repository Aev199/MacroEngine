namespace MacroEngine.Core;

/// <summary>
/// Well-known OS / application hotkeys that MacroEngine must never intercept.
/// They always take priority: such combinations cannot be assigned as triggers
/// or formed by a leader chord + rest, and are never swallowed at runtime.
///
/// Key names follow <see cref="KeyInterceptor.VkToName"/> output (e.g. "Escape",
/// "Delete", "Tab") so runtime combos compare correctly.
/// </summary>
internal static class SystemHotkeys
{
    private static readonly HashSet<string> _set = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+Z", "Ctrl+Y",
        "Ctrl+A", "Ctrl+S", "Ctrl+W", "Ctrl+Q", "Ctrl+F",
        "Ctrl+N", "Ctrl+O", "Ctrl+P", "Ctrl+H", "Ctrl+T",
        "Alt+Tab", "Alt+F4", "Ctrl+Escape", "Ctrl+Shift+Escape",
        "Ctrl+Alt+Delete",
    };

    /// <summary>True if <paramref name="combo"/> (e.g. "Ctrl+Alt+Delete") is a reserved system hotkey.</summary>
    public static bool IsSystem(string combo) =>
        !string.IsNullOrEmpty(combo) && _set.Contains(combo.Replace(" ", ""));
}
