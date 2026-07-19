using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using MacroEngine.Core;

namespace MacroEngine.UI;

/// <summary>
/// Modal window that captures a hotkey via its own KeyDown/KeyUp —
/// does not use the global hook.
///
/// Normal mode (Шорткат):
///   - Ctrl/Alt/Shift + any key: committed when the first modifier is released.
///   - Standalone F1–F24: committed immediately on key down.
///
/// Leader mode:
///   - A chord of 2–3 modifiers only (e.g. Ctrl+Alt). Committed once the user
///     starts releasing the chord.
///
/// Close via <c>ShowDialog&lt;string?&gt;(owner)</c>: returns the combo or null.
/// </summary>
internal sealed class HotkeyRecorderWindow : Window
{
    private readonly TextBlock _label;
    private readonly bool _leaderMode;

    private readonly List<string> _capturedMods = new();
    private readonly HashSet<string> _heldMods = new();
    private string _mainKey = "";

    public HotkeyRecorderWindow(bool leaderMode = false)
    {
        _leaderMode = leaderMode;

        Title = leaderMode ? "Запись лидер-аккорда" : "Запись сочетания";
        Width = 380;
        Height = 160;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Icon = AppIcon.Get();

        _label = new TextBlock
        {
            Text = DefaultPrompt(),
            FontSize = 15,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Content = new Border { Padding = new Thickness(16), Child = _label };

        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
    }

    // ── Key handling ────────────────────────────────────────────────

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            Close(null);
            return;
        }

        string? mod = ModName(e.Key);
        if (mod != null) _heldMods.Add(mod);

        if (_leaderMode)
        {
            // Track the maximal modifier set held simultaneously; ignore other keys.
            var held = OrderedHeldMods();
            if (held.Count > _capturedMods.Count)
            {
                _capturedMods.Clear();
                _capturedMods.AddRange(held);
            }
            UpdateLabel();
            return;
        }

        if (mod != null)
        {
            // Modifier pressed while no main key is captured yet — reset for a fresh attempt.
            if (_mainKey.Length == 0)
                _capturedMods.Clear();
            UpdateLabel();
            return;
        }

        string name = KeyToName(e.Key);
        if (name.Length == 0) return;

        // Capture the active modifiers and the pressed key.
        _capturedMods.Clear();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) _capturedMods.Add("Ctrl");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))     _capturedMods.Add("Alt");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))   _capturedMods.Add("Shift");
        _mainKey = name;

        UpdateLabel();

        // Standalone F-key: commit immediately without waiting for a modifier release.
        if (_capturedMods.Count == 0 && e.Key >= Key.F1 && e.Key <= Key.F24)
            Close(_mainKey);
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        string? mod = ModName(e.Key);
        if (mod == null) return;
        _heldMods.Remove(mod);

        if (_leaderMode)
        {
            // Commit the modifier chord once the user starts releasing it (needs ≥2 mods).
            if (_capturedMods.Count >= 2)
                Close(string.Join("+", _capturedMods));
            return;
        }

        // A modifier was released — commit if we already have a main key with modifiers.
        if (_mainKey.Length > 0 && _capturedMods.Count > 0)
        {
            string combo = string.Join("+", _capturedMods.Append(_mainKey));

            // System hotkeys have the highest priority and cannot be assigned.
            if (SystemHotkeys.IsSystem(combo))
            {
                _label.Text = $"{combo} — системное сочетание,\nнедоступно. Выберите другое.";
                _capturedMods.Clear();
                _mainKey = "";
                return;
            }

            Close(combo);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private List<string> OrderedHeldMods()
    {
        var list = new List<string>();
        if (_heldMods.Contains("Ctrl"))  list.Add("Ctrl");
        if (_heldMods.Contains("Alt"))   list.Add("Alt");
        if (_heldMods.Contains("Shift")) list.Add("Shift");
        return list;
    }

    private void UpdateLabel()
    {
        if (_leaderMode)
        {
            _label.Text = _capturedMods.Count > 0
                ? string.Join("+", _capturedMods) + (_capturedMods.Count >= 2 ? "   ✓ отпустите" : "+…")
                : DefaultPrompt();
            return;
        }

        if (_mainKey.Length > 0 && _capturedMods.Count > 0)
        {
            _label.Text = string.Join("+", _capturedMods.Append(_mainKey));
            return;
        }

        var held = OrderedHeldMods();
        _label.Text = held.Count > 0 ? string.Join("+", held) + "+…" : DefaultPrompt();
    }

    private string DefaultPrompt() => _leaderMode
        ? "Зажмите 2–3 модификатора\n(Ctrl / Alt / Shift), затем отпустите\n(Esc — отмена)"
        : "Нажмите Ctrl/Alt + клавишу…\nили F1–F12 без модификаторов\n(Esc — отмена)";

    private static string? ModName(Key k) => k switch
    {
        Key.LeftCtrl or Key.RightCtrl   => "Ctrl",
        Key.LeftAlt or Key.RightAlt     => "Alt",
        Key.LeftShift or Key.RightShift => "Shift",
        Key.LWin or Key.RWin            => "Win",
        _ => null
    };

    private static string KeyToName(Key k)
    {
        if (k >= Key.A && k <= Key.Z)   return k.ToString();
        if (k >= Key.D0 && k <= Key.D9) return ((char)('0' + (k - Key.D0))).ToString();
        if (k >= Key.F1 && k <= Key.F24) return k.ToString();
        return k switch
        {
            Key.Space    => "Space",
            Key.Enter    => "Enter",
            Key.Tab      => "Tab",
            Key.Back     => "Back",
            Key.Delete   => "Delete",
            Key.Insert   => "Insert",
            Key.Left     => "Left",
            Key.Up       => "Up",
            Key.Right    => "Right",
            Key.Down     => "Down",
            Key.Home     => "Home",
            Key.End      => "End",
            Key.PageUp   => "PageUp",
            Key.PageDown => "PageDown",
            _ => ""
        };
    }
}
