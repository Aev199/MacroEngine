using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MacroEngine.Core;

namespace MacroEngine.UI;

/// <summary>Offline help backed by the README embedded in the executable.</summary>
internal sealed class HelpWindow : Window
{
    public HelpWindow(Action openAbout)
    {
        Title = $"MacroEngine {ProductInfo.DisplayVersion} — Справка";
        Width = 820;
        Height = 650;
        MinWidth = 620;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = AppIcon.Get();

        var tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Быстрый старт", Content = BuildQuickStart() },
                new TabItem { Header = "README", Content = BuildReadme() },
            }
        };

        var data = new Button { Content = "Папка данных", Classes = { "accent" } };
        var about = new Button { Content = "О программе" };
        var close = new Button { Content = "Закрыть", IsCancel = true };
        var status = new TextBlock
        {
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Text = "Настройки открываются левым кликом по иконке MacroEngine в трее."
        };

        about.Click += (_, _) => openAbout();
        close.Click += (_, _) => Close();
        data.Click += (_, _) => Run(status, () => ShellTools.OpenDirectory(AppPaths.RootDirectory), "Папка данных открыта.");

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
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
            Margin = new Thickness(12),
            Padding = new Thickness(12)
        };

        return readme;
    }

    private static Control BuildQuickStart()
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12
        };

        panel.Children.Add(TitleText("MacroEngine — локальная автоматизация Windows"));
        panel.Children.Add(Paragraph(
            "Программа живёт в системном трее. Левый клик открывает настройки, правый — меню управления, справку, папки данных и аварийную остановку."));

        panel.Children.Add(Section("1. Создайте триггер"));
        panel.Children.Add(Paragraph(
            "В настройках добавьте строку, выберите тип и действие. Для обычной подстановки используйте тип «Текст», например !mail → адрес электронной почты."));
        panel.Children.Add(Code("!mail   →   name@example.com\n!date   →   {date}\n!sig    →   С уважением,\\nИмя"));

        panel.Children.Add(Section("2. Выберите способ запуска"));
        panel.Children.Add(Bullet("Текст — срабатывает после набора последовательности символов."));
        panel.Children.Add(Bullet("Шорткат — Ctrl или Alt с клавишей; F1–F24 можно назначать отдельно."));
        panel.Children.Add(Bullet("Лидер — удерживаемый аккорд из 2–3 модификаторов и короткая последовательность клавиш."));

        panel.Children.Add(Section("3. Ограничьте контекст"));
        panel.Children.Add(Paragraph(
            "Контекст «*» работает везде. Значение acad ограничивает триггер окнами AutoCAD. Несколько значений разделяются запятыми; !browser исключает совпавшие окна."));

        panel.Children.Add(Section("4. Используйте токены"));
        panel.Children.Add(Code(
            "{date}  {time}  {datetime:yyyy-MM-dd}\n{clipboard}  {input:Подпись}  {choice:Да|Нет}\n{cursor}"));

        panel.Children.Add(Section("5. Макросы"));
        panel.Children.Add(Code(
            "type текст\nkey Ctrl+S\nsleep 500\nclick 120,300\nrun \"C:\\Program Files\\Tool\\tool.exe\" --arg"));
        panel.Children.Add(Paragraph(
            "Пока выполняется одно действие, новый запуск отклоняется. При смене активного окна ввод автоматически прекращается, чтобы не попасть в другое приложение."));

        panel.Children.Add(Section("Диагностика"));
        panel.Children.Add(Paragraph(
            "Обычный лог не содержит введённый текст, значения триггеров, команды и содержимое буфера обмена. Подробное логирование включайте только временно через меню трея."));

        return new ScrollViewer { Content = panel };
    }

    private static TextBlock TitleText(string text) => new()
    {
        Text = text,
        FontSize = 24,
        FontWeight = FontWeight.SemiBold,
        TextWrapping = TextWrapping.Wrap
    };

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontSize = 17,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 8, 0, 0),
        TextWrapping = TextWrapping.Wrap
    };

    private static TextBlock Paragraph(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 21
    };

    private static TextBlock Bullet(string text) => new()
    {
        Text = "• " + text,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(8, 0, 0, 0)
    };

    private static Border Code(string text) => new()
    {
        Padding = new Thickness(12),
        CornerRadius = new CornerRadius(6),
        Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
        Child = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
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
