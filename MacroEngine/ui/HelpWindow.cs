using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MacroEngine.Core;

namespace MacroEngine.UI;

/// <summary>Offline help backed by the README embedded in the executable.</summary>
internal sealed class HelpWindow : Window
{
    private static readonly FontFamily MonoFont =
        new("Cascadia Mono,Consolas,monospace");

    public HelpWindow(Action openAbout)
    {
        Title = $"MacroEngine {ProductInfo.DisplayVersion} — Справка";
        Width = 780;
        Height = 590;
        MinWidth = 600;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = AppIcon.Get();

        var tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Кратко", Content = BuildQuickReference() },
                new TabItem { Header = "README", Content = BuildReadme() }
            }
        };

        var data = new Button { Content = "Папка данных" };
        var about = new Button { Content = "О программе" };
        var close = new Button { Content = "Закрыть", IsCancel = true };
        var status = new TextBlock
        {
            Opacity = 0.54,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };

        about.Click += (_, _) => openAbout();
        close.Click += (_, _) => Close();
        data.Click += (_, _) => Run(
            status,
            () => ShellTools.OpenDirectory(AppPaths.RootDirectory),
            "Папка данных открыта.");

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(10, 7, 10, 10)
        };
        footer.Children.Add(status);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { data, about, close }
        };
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children = { tabs, footer }
        };
        Grid.SetRow(footer, 1);
    }

    private static Control BuildReadme() => new TextBox
    {
        Text = ProductInfo.ReadEmbeddedReadme(),
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(8),
        Padding = new Thickness(8)
    };

    private static Control BuildQuickReference()
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(18, 14),
            Spacing = 10
        };

        panel.Children.Add(Section("Триггеры"));
        panel.Children.Add(Code(
            "!mail        name@example.com\n" +
            "!date        {date}\n" +
            "Ctrl+Alt+M   macro: AcadSave"));
        panel.Children.Add(Note(
            "Текст — последовательность символов. Шорткат — прямое сочетание. Лидер — 2–3 модификатора и короткий остаток."));

        panel.Children.Add(Divider());
        panel.Children.Add(Section("Контекст"));
        panel.Children.Add(Code("*            везде\nacad         AutoCAD\n!browser     исключить браузеры"));
        panel.Children.Add(Note("Несколько значений разделяются запятыми."));

        panel.Children.Add(Divider());
        panel.Children.Add(Section("Токены"));
        panel.Children.Add(Code(
            "{date}  {time}  {datetime:yyyy-MM-dd}\n" +
            "{clipboard}  {input:Подпись}  {choice:Да|Нет}  {cursor}"));

        panel.Children.Add(Divider());
        panel.Children.Add(Section("Макросы"));
        panel.Children.Add(Code(
            "type текст\n" +
            "key Ctrl+S\n" +
            "sleep 500\n" +
            "click 120,300\n" +
            "run \"C:\\Program Files\\Tool\\tool.exe\" --arg"));

        panel.Children.Add(Divider());
        panel.Children.Add(Section("Безопасность"));
        panel.Children.Add(Note(
            "Одновременно выполняется одно действие. При смене исходного окна ввод прекращается. Обычный журнал не сохраняет текст подстановок, команды или буфер обмена."));

        return new ScrollViewer { Content = panel };
    }

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = FontWeight.Medium
    };

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.68,
        LineHeight = 19
    };

    private static TextBlock Code(string text) => new()
    {
        Text = text,
        FontFamily = MonoFont,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(8, 0, 0, 0)
    };

    private static Border Divider() => new()
    {
        Height = 1,
        Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
        Margin = new Thickness(0, 2)
    };

    private static void Run(TextBlock status, Action action, string success)
    {
        try
        {
            action();
            status.Text = success;
        }
        catch (Exception ex)
        {
            status.Text = $"Ошибка: {ex.Message}";
        }
    }
}
