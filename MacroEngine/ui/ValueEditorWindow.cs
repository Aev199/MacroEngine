using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MacroEngine.UI;

/// <summary>
/// Focused editor for a trigger value. Syntax help lives in the main settings/help UI,
/// so this dialog contains only the value being edited and its actions.
/// </summary>
internal sealed class ValueEditorWindow : Window
{
    private readonly TextBox? _editor;
    private readonly ComboBox? _macroCombo;

    public ValueEditorWindow(
        string currentValue,
        string triggerName,
        IReadOnlyList<string>? macroNames = null)
    {
        bool macroMode = macroNames != null;

        Title = $"Значение — {triggerName}";
        Width = 540;
        Height = macroMode ? 190 : 360;
        MinWidth = 400;
        MinHeight = macroMode ? 190 : 250;
        CanResize = !macroMode;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Icon = AppIcon.Get();

        Control body;

        if (macroMode)
        {
            var names = macroNames!
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            int selectedIndex = names.FindIndex(n =>
                string.Equals(n, currentValue, StringComparison.OrdinalIgnoreCase));

            bool missingCurrent = currentValue.Length > 0 && selectedIndex < 0;
            if (missingCurrent)
            {
                names.Insert(0, currentValue);
                selectedIndex = 0;
            }
            else if (selectedIndex < 0 && names.Count > 0)
            {
                selectedIndex = 0;
            }

            _macroCombo = new ComboBox
            {
                ItemsSource = names,
                SelectedIndex = selectedIndex,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var status = new TextBlock
            {
                Text = missingCurrent
                    ? "Текущий макрос отсутствует в библиотеке; значение сохранено, чтобы не потерять конфигурацию."
                    : names.Count == 0
                        ? "Сначала создайте макрос на вкладке «Макросы»."
                        : "",
                IsVisible = missingCurrent || names.Count == 0,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12
            };

            body = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = "Макрос", Opacity = 0.6, FontSize = 12 },
                    _macroCombo,
                    status
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

        var btnOk = new Button
        {
            Content = "Сохранить",
            MinWidth = 92,
            IsDefault = true,
            IsEnabled = !macroMode || _macroCombo!.SelectedIndex >= 0,
            Classes = { "accent" }
        };
        var btnCancel = new Button { Content = "Отмена", MinWidth = 88, IsCancel = true };

        if (_macroCombo != null)
            _macroCombo.SelectionChanged += (_, _) => btnOk.IsEnabled = _macroCombo.SelectedIndex >= 0;

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
        buttons.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(buttons);
        root.Children.Add(body);
        Content = root;

        Opened += (_, _) => (_editor as Control ?? _macroCombo)?.Focus();
    }

    private string CurrentValue() => _macroCombo != null
        ? _macroCombo.SelectedItem?.ToString() ?? ""
        : (_editor?.Text ?? "").Replace("\r\n", "\n");
}
