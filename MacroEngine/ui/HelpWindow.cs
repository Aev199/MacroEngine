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
        Width = 800;
        Height = 620;
        MinWidth = 620;
        MinHeight = 460;
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
            Opacity = 0.58,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Text = "Настройки: левый клик по значку MacroEngine в трее."
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
            Margin = new Thickness(12, 8, 12, 12)
        };
        footer.Children.Add(status);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
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

    private static Control BuildReadme()
    {
        var readme = new TextBox
        {
            Text = ProductInfo.ReadEmbeddedReadme(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(10),
            Padding = new Thickness(10)
        };

        return readme;
    }

    private static Control BuildQuickReference()
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14
        };

        panel.Children.Add(new TextBlock
        {
            Text = "MacroEngine",
            FontSize = 23,
            FontWeight = FontWeight.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Текстовые триггеры, сочетания клавиш и последовательные макросы.",
            Opacity = 0.68,
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(Section("Триггеры"));
        panel.Children.Add(Code(
            "!mail      name@example.com\n" +
            "!date      {date}\n" +
            "Ctrl+Alt+M macro: AcadSave"));
        panel.Children.Add(Note(
            "Текст срабатывает после набора последовательности. Шорткат запускается прямым сочетанием. Лидер использует 2–3 модификатора и короткий остаток."));

        panel.Children.Add(Section("Контекст"));
        panel.Children.Add(Code("*          везде\nacad       AutoCAD\n!browser   исключить браузеры"));
        panel.Children.Add(Note("Несколько значений разделяются запятыми."));

        panel.Children.Add(Section("Токены"));
        panel.Children.Add(Code(
            "{date}  {time}  {datetime:yyyy-MM-dd}\n" +
            "{clipboard}  {input:Подпись}  {choice:Да|Нет}  {cursor}"));

        panel.Children.Add(Section("Макросы"));
        panel.Children.Add(Code(
            "type текст\n" +
            "key Ctrl+S\n" +
            "sleep 500\n" +
            "click 120,300\n" +
            "run \"C:\\Program Files\\Tool\\tool.exe\" --arg"));

        panel.Children.Add(Section("Безопасность"));
        panel.Children.Add(Note(
            "Одновременно выполняется только одно действие. При смене исходного окна ввод прекращается. Обычный журнал не сохраняет пользовательский текст, команды или буфер обмена."));

        return new ScrollViewer { Content = panel };
    }

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 5, 0, 0)
    };

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.72,
        LineHeight = 20
    };

    private static Border Code(string text) => new()
    {
        Padding = new Thickness(10, 8),
        CornerRadius = new CornerRadius(4),
        Background = new SolidColorBrush(Color.FromArgb(38, 0, 0, 0)),
        Child = new TextBlock
        {
            Text = text,
            FontFamily = MonoFont,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        }
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
