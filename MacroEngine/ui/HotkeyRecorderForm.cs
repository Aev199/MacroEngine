using MacroEngine.Core;

namespace MacroEngine.UI;

/// <summary>
/// Tiny modal window that captures a hotkey combo from the user.
/// Listens to its own KeyDown — no global hook needed.
/// </summary>
internal sealed class HotkeyRecorderForm : Form
{
    private readonly Label _label;
    private readonly HashSet<string> _mods = new();
    private readonly List<string> _keys = new(); // supports multi-key like Ctrl+A+F

    public string CapturedCombo { get; private set; } = "";

    public HotkeyRecorderForm()
    {
        Text = "Запись сочетания";
        Size = new Size(300, 120);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        KeyPreview = true;
        Icon = AppIcon.Get();

        _label = new Label
        {
            Text = "Нажмите сочетание клавиш...\n(Escape — отмена)",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 11)
        };

        Controls.Add(_label);
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;

        // Track modifiers
        _mods.Clear();
        if (e.Control) _mods.Add("Ctrl");
        if (e.Alt) _mods.Add("Alt");
        if (e.Shift) _mods.Add("Shift");

        // Cancel on Escape (without modifiers)
        if (e.KeyCode == Keys.Escape && _mods.Count == 0)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        string name = KeyCodeToName(e.KeyCode);
        if (name.Length > 0 && !_keys.Contains(name))
            _keys.Add(name);

        UpdateLabel();
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        // Commit when modifiers are released AND we have at least one key
        if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.LControlKey || e.KeyCode == Keys.RControlKey ||
            e.KeyCode == Keys.Menu || e.KeyCode == Keys.LMenu || e.KeyCode == Keys.RMenu ||
            e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.LShiftKey || e.KeyCode == Keys.RShiftKey)
        {
            if (_keys.Count > 0 && _mods.Count > 0)
            {
                CapturedCombo = string.Join("+", _mods.OrderByDescending(m => m == "Ctrl").ThenByDescending(m => m == "Alt").ThenBy(m => m == "Shift").Concat(_keys));
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }

    private void UpdateLabel()
    {
        if (_keys.Count == 0)
            _label.Text = _mods.Count > 0 ? string.Join("+", _mods) + "+…" : "Нажмите Ctrl/Alt...";
        else
            _label.Text = string.Join("+", _mods.Concat(_keys));
    }

    private static string KeyCodeToName(Keys k)
    {
        if (k >= Keys.A && k <= Keys.Z) return k.ToString();
        if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
        if (k >= Keys.F1 && k <= Keys.F24) return k.ToString();
        return k switch
        {
            Keys.Space => "Space",
            Keys.Enter => "Enter",
            Keys.Escape => "Escape",
            Keys.Tab => "Tab",
            Keys.Back => "Back",
            Keys.Delete => "Delete",
            Keys.Insert => "Insert",
            Keys.Left => "Left",
            Keys.Up => "Up",
            Keys.Right => "Right",
            Keys.Down => "Down",
            _ => ""
        };
    }
}
