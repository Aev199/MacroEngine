using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MacroEngine.UI;

/// <summary>
/// Modal editor for a trigger's value. Two modes:
///   text  — multiline monospace editor with a token hint;
///   macro — dropdown of named macros (for action="macro").
/// Close via <c>ShowDialog&lt;string?&gt;(owner)</c>: returns the value or null if cancelled.
/// </summary>
internal sealed class ValueEditorWindow : Window
{
    private readonly TextBox? _editor;
    private readonly ComboBox? _macroCombo;

    public ValueEditorWindow(string currentValue, string triggerName,
                             IReadOnlyList<string>? macroNames = null)
    {
        bool macroMode = macroNames != null;

        Title = $"Значение — {triggerName}";
        Width = 540;
        Height = macroMode ? 190 : 380;
        MinWidth = 400;
        MinHeight = macroMode ? 190 : 260;
        CanResize = !macroMode;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Icon = AppIcon.Get();

        Control body;
        if (macroMode)
        {
            _macroCombo = new ComboBox
            {
                ItemsSource = macroNames,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            int idx = -1;
            for (int i = 0; i < macroNames!.Count; i++)
                if (string.Equals(macroNames[i], currentValue, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
            _macroCombo.SelectedIndex = idx >= 0 ? idx : (macroNames.Count > 0 ? 0 : -1);

            body = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Макрос (вкладка «Макросы»):", Opacity = 0.7 },
                    _macroCombo
                }
            };
        }
        else
        {
            _editor = new TextBox
            {
                Text = currentValue,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
                VerticalAlignment = VerticalAlignment.Stretch
            };
            body = _editor;
        }

        var hint = new TextBlock
        {
            Text = "Токены: {date} {time} {clipboard} {cursor} {input:подпись} {choice:a|b|c}",
            Opacity = 0.6,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = !macroMode
        };

        var btnOk = new Button { Content = "OK", Width = 90, IsDefault = macroMode };
        var btnCancel = new Button { Content = "Отмена", Width = 90, IsCancel = true };
        btnOk.Click += (_, _) => Close(CurrentValue());
        btnCancel.Click += (_, _) => Close(null);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { btnOk, btnCancel }
        };

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(hint, Dock.Bottom);
        buttons.Margin = new Thickness(0, 10, 0, 0);
        hint.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(buttons);
        root.Children.Add(hint);
        root.Children.Add(body);
        Content = root;

        Opened += (_, _) => (_editor as Control ?? _macroCombo)?.Focus();
    }

    private string CurrentValue() => _macroCombo != null
        ? _macroCombo.SelectedItem?.ToString() ?? ""
        : (_editor?.Text ?? "").Replace("\r\n", "\n");
}
