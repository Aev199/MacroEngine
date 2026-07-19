using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MacroEngine.UI;

/// <summary>
/// Modal editor for a trigger value. Text actions use a multiline editor;
/// macro actions use a picker of named macros while preserving missing or
/// legacy inline values instead of silently replacing them.
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
        Height = macroMode ? 210 : 380;
        MinWidth = 400;
        MinHeight = macroMode ? 210 : 260;
        CanResize = !macroMode;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Icon = AppIcon.Get();

        TextBlock? macroWarning = null;
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

            macroWarning = new TextBlock
            {
                Text = missingCurrent
                    ? "Текущее значение не найдено в библиотеке макросов. Оно сохранено в списке, чтобы не потерять конфигурацию."
                    : names.Count == 0
                        ? "Сначала создайте макрос на вкладке «Макросы»."
                        : "",
                IsVisible = missingCurrent || names.Count == 0,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 190, 100)),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12
            };

            body = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Макрос (вкладка «Макросы»):",
                        Opacity = 0.7
                    },
                    _macroCombo,
                    macroWarning
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

        var btnOk = new Button
        {
            Content = "OK",
            Width = 90,
            IsDefault = macroMode,
            IsEnabled = !macroMode || _macroCombo!.SelectedIndex >= 0
        };
        var btnCancel = new Button { Content = "Отмена", Width = 90, IsCancel = true };

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
