using MacroEngine.Core;

namespace MacroEngine.UI;

/// <summary>
/// Modal window that captures a hotkey from the user via its own KeyDown/KeyUp —
/// does not use the global hook.
///
/// Normal mode (Шорткат):
///   - Ctrl/Alt/Shift + any key: committed when the first modifier is released.
///   - Standalone F1–F24: committed immediately on key down.
///
/// Leader mode:
///   - A chord of 2–3 modifiers only (e.g. Ctrl+Alt). Committed once the user
///     starts releasing the chord. The "rest" of the combination is typed
///     separately (in the trigger field) while the chord is held.
/// </summary>
internal sealed class HotkeyRecorderForm : Form
{
    private readonly Label _label;
    private readonly bool _leaderMode;

    // Modifiers held at the moment _mainKey was pressed (defines the combo);
    // in leader mode this holds the maximal modifier set seen so far.
    private readonly List<string> _capturedMods = new();
    private string _mainKey = "";

    public string CapturedCombo { get; private set; } = "";

    public HotkeyRecorderForm(bool leaderMode = false)
    {
        _leaderMode = leaderMode;

        Text = leaderMode ? "Запись лидер-аккорда" : "Запись сочетания";
        Size = new Size(360, 150);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        KeyPreview = true;
        Icon = AppIcon.Get();

        _label = new Label
        {
            Text = DefaultPrompt(),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 11)
        };

        Controls.Add(_label);
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
    }

    // ── Key handling ────────────────────────────────────────────────

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;

        if (e.KeyCode == Keys.Escape && !e.Control && !e.Alt && !e.Shift)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (_leaderMode)
        {
            // Track the maximal modifier set held simultaneously; ignore other keys.
            var held = CurrentMods();
            if (held.Count > _capturedMods.Count)
            {
                _capturedMods.Clear();
                _capturedMods.AddRange(held);
            }
            UpdateLabel();
            return;
        }

        if (IsModifierKey(e.KeyCode))
        {
            // Modifier pressed while no main key is captured yet — reset for a fresh attempt.
            if (_mainKey.Length == 0)
                _capturedMods.Clear();
            UpdateLabel();
            return;
        }

        string name = KeyCodeToName(e.KeyCode);
        if (name.Length == 0) return;

        // Capture the active modifiers and the pressed key.
        _capturedMods.Clear();
        if (e.Control) _capturedMods.Add("Ctrl");
        if (e.Alt)     _capturedMods.Add("Alt");
        if (e.Shift)   _capturedMods.Add("Shift");
        _mainKey = name;

        UpdateLabel();

        // Standalone F-key: commit immediately without waiting for a modifier release.
        if (_capturedMods.Count == 0 && IsFunctionKey(e.KeyCode))
        {
            CapturedCombo = _mainKey;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (!IsModifierKey(e.KeyCode)) return;

        if (_leaderMode)
        {
            // Commit the modifier chord once the user starts releasing it (needs ≥2 mods).
            if (_capturedMods.Count >= 2)
            {
                CapturedCombo = string.Join("+", _capturedMods);
                DialogResult = DialogResult.OK;
                Close();
            }
            return;
        }

        // A modifier was released — commit if we already have a main key with modifiers.
        if (_mainKey.Length > 0 && _capturedMods.Count > 0)
        {
            string combo = BuildCombo();

            // System hotkeys have the highest priority and cannot be assigned.
            if (SystemHotkeys.IsSystem(combo))
            {
                _label.Text = $"{combo} — системное сочетание,\nнедоступно. Выберите другое.";
                _capturedMods.Clear();
                _mainKey = "";
                return;
            }

            CapturedCombo = combo;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static List<string> CurrentMods()
    {
        var held = new List<string>();
        if ((ModifierKeys & Keys.Control) != 0) held.Add("Ctrl");
        if ((ModifierKeys & Keys.Alt)     != 0) held.Add("Alt");
        if ((ModifierKeys & Keys.Shift)   != 0) held.Add("Shift");
        return held;
    }

    private string BuildCombo() =>
        string.Join("+", _capturedMods.Append(_mainKey));

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
            _label.Text = BuildCombo();
            return;
        }

        // Show which modifier keys are currently held.
        var held = CurrentMods();
        _label.Text = held.Count > 0
            ? string.Join("+", held) + "+…"
            : DefaultPrompt();
    }

    private string DefaultPrompt() => _leaderMode
        ? "Зажмите 2–3 модификатора\n(Ctrl / Alt / Shift), затем отпустите\n(Esc — отмена)"
        : "Нажмите Ctrl/Alt + клавишу…\nили F1–F12 без модификаторов\n(Esc — отмена)";

    private static bool IsModifierKey(Keys k) =>
        k is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
          or Keys.Menu       or Keys.LMenu       or Keys.RMenu
          or Keys.ShiftKey   or Keys.LShiftKey   or Keys.RShiftKey
          or Keys.LWin       or Keys.RWin;

    private static bool IsFunctionKey(Keys k) => k >= Keys.F1 && k <= Keys.F24;

    private static string KeyCodeToName(Keys k)
    {
        if (k >= Keys.A  && k <= Keys.Z)   return k.ToString();
        if (k >= Keys.D0 && k <= Keys.D9)  return ((char)('0' + (k - Keys.D0))).ToString();
        if (k >= Keys.F1 && k <= Keys.F24) return k.ToString();
        return k switch
        {
            Keys.Space    => "Space",
            Keys.Enter    => "Enter",
            Keys.Tab      => "Tab",
            Keys.Back     => "Back",
            Keys.Delete   => "Delete",
            Keys.Insert   => "Insert",
            Keys.Left     => "Left",
            Keys.Up       => "Up",
            Keys.Right    => "Right",
            Keys.Down     => "Down",
            Keys.Home     => "Home",
            Keys.End      => "End",
            Keys.PageUp   => "PageUp",
            Keys.PageDown => "PageDown",
            _ => ""
        };
    }
}