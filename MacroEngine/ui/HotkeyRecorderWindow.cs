using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MacroEngine.Core;

namespace MacroEngine.UI;

/// <summary>
/// Modal window that captures a shortcut or leader chord from its own key events.
/// The global hook is not involved.
/// </summary>
internal sealed class HotkeyRecorderWindow : Window
{
    private static readonly FontFamily MonoFont =
        new("Cascadia Mono,Consolas,monospace");

    private readonly TextBlock _capture;
    private readonly TextBlock _instruction;
    private readonly bool _leaderMode;

    private readonly List<string> _capturedMods = new();
    private readonly HashSet<string> _heldMods = new();
    private string _mainKey = "";

    public HotkeyRecorderWindow(bool leaderMode = false)
    {
        _leaderMode = leaderMode;

        Title = leaderMode ? "Лидер" : "Сочетание клавиш";
        Width = 380;
        Height = 150;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Icon = AppIcon.Get();

        _capture = new TextBlock
        {
            Text = "—",
            FontSize = 19,
            FontFamily = MonoFont,
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _instruction = new TextBlock
        {
            Text = DefaultPrompt(),
            FontSize = 12,
            Opacity = 0.62,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _capture, _instruction }
        };

        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
    }

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
            var held = OrderedHeldMods();
            if (held.Count > _capturedMods.Count)
            {
                _capturedMods.Clear();
                _capturedMods.AddRange(held);
            }
            UpdateDisplay();
            return;
        }

        if (mod != null)
        {
            if (_mainKey.Length == 0)
                _capturedMods.Clear();
            UpdateDisplay();
            return;
        }

        string name = KeyToName(e.Key);
        if (name.Length == 0) return;

        _capturedMods.Clear();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) _capturedMods.Add("Ctrl");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) _capturedMods.Add("Alt");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _capturedMods.Add("Shift");
        _mainKey = name;

        UpdateDisplay();

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
            if (_capturedMods.Count >= 2)
                Close(string.Join("+", _capturedMods));
            return;
        }

        if (_mainKey.Length > 0 && _capturedMods.Count > 0)
        {
            string combo = string.Join("+", _capturedMods.Append(_mainKey));

            if (!HotkeyRules.TryValidateShortcut(combo, out string validationError))
            {
                ShowError(combo, validationError);
                return;
            }

            if (SystemHotkeys.IsSystem(combo))
            {
                ShowError(combo, "Системное сочетание. Выберите другое.");
                return;
            }

            Close(combo);
        }
    }

    private void ShowError(string combo, string message)
    {
        _capture.Text = combo;
        _instruction.Text = message + "  Esc — отмена.";
        _capturedMods.Clear();
        _heldMods.Clear();
        _mainKey = "";
    }

    private List<string> OrderedHeldMods()
    {
        var list = new List<string>();
        if (_heldMods.Contains("Ctrl")) list.Add("Ctrl");
        if (_heldMods.Contains("Alt")) list.Add("Alt");
        if (_heldMods.Contains("Shift")) list.Add("Shift");
        return list;
    }

    private void UpdateDisplay()
    {
        if (_leaderMode)
        {
            _capture.Text = _capturedMods.Count > 0
                ? string.Join("+", _capturedMods)
                : "—";
            _instruction.Text = _capturedMods.Count >= 2
                ? "Отпустите клавиши, чтобы сохранить.  Esc — отмена."
                : DefaultPrompt();
            return;
        }

        if (_mainKey.Length > 0 && _capturedMods.Count > 0)
        {
            _capture.Text = string.Join("+", _capturedMods.Append(_mainKey));
            _instruction.Text = "Отпустите модификатор, чтобы сохранить.  Esc — отмена.";
            return;
        }

        var held = OrderedHeldMods();
        _capture.Text = held.Count > 0 ? string.Join("+", held) : "—";
        _instruction.Text = DefaultPrompt();
    }

    private string DefaultPrompt() => _leaderMode
        ? "Зажмите 2–3 клавиши Ctrl / Alt / Shift.  Esc — отмена."
        : "Нажмите Ctrl/Alt + клавишу или F1–F24.  Esc — отмена.";

    private static string? ModName(Key k) => k switch
    {
        Key.LeftCtrl or Key.RightCtrl => "Ctrl",
        Key.LeftAlt or Key.RightAlt => "Alt",
        Key.LeftShift or Key.RightShift => "Shift",
        Key.LWin or Key.RWin => "Win",
        _ => null
    };

    private static string KeyToName(Key k)
    {
        if (k >= Key.A && k <= Key.Z) return k.ToString();
        if (k >= Key.D0 && k <= Key.D9) return ((char)('0' + (k - Key.D0))).ToString();
        if (k >= Key.F1 && k <= Key.F24) return k.ToString();
        return k switch
        {
            Key.Space => "Space",
            Key.Enter => "Enter",
            Key.Tab => "Tab",
            Key.Back => "Back",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Left => "Left",
            Key.Up => "Up",
            Key.Right => "Right",
            Key.Down => "Down",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            _ => ""
        };
    }
}
