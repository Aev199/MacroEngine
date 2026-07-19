using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MacroEngine.Core;

namespace MacroEngine.UI;

/// <summary>Product information, support paths and privacy-safe diagnostics.</summary>
internal sealed class AboutWindow : Window
{
    private readonly TextBlock _status;

    public AboutWindow(Action openHelp)
    {
        Title = "MacroEngine — О программе";
        Width = 650;
        Height = 570;
        MinWidth = 560;
        MinHeight = 480;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = AppIcon.Get();

        _status = new TextBlock
        {
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        var header = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "MacroEngine",
                    FontSize = 28,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = ProductInfo.DisplayVersion + " · personal beta",
                    FontSize = 15,
                    Opacity = 0.75
                },
                new TextBlock
                {
                    Text = "Локальный Windows-инструмент для текстовых подстановок, контекстных сочетаний и последовательных макросов.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0)
                }
            }
        };

        var privacy = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(45, 0, 210, 160)),
            Child = new TextBlock
            {
                Text = AppLog.DiagnosticEnabled
                    ? "Диагностическое логирование включено. Оно может содержать сведения о клавишах, раскладке и активных окнах."
                    : "Обычный лог не содержит введённый текст, значения триггеров, команды, макросы или содержимое буфера обмена.",
                TextWrapping = TextWrapping.Wrap
            }
        };

        var paths = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Section("Хранение данных"),
                PathBlock("Режим", AppPaths.IsPortable ? "Portable — рядом с EXE" : "LocalAppData"),
                PathBlock("Программа", AppPaths.ApplicationDirectory),
                PathBlock("Данные", AppPaths.RootDirectory),
                PathBlock("Триггеры", AppPaths.TriggersFile),
                PathBlock("Макросы", AppPaths.MacrosFile),
                PathBlock("Журнал", AppPaths.LogFile)
            }
        };

        var openData = new Button { Content = "Папка данных", Classes = { "accent" } };
        var openConfig = new Button { Content = "Конфиги" };
        var openLogs = new Button { Content = "Логи" };
        var openLog = new Button { Content = "Открыть журнал" };
        var copyDiagnostics = new Button { Content = "Копировать диагностику" };
        var help = new Button { Content = "Справка" };
        var github = new Button { Content = "GitHub" };
        var close = new Button { Content = "Закрыть", IsCancel = true };

        openData.Click += (_, _) => Run(() => ShellTools.OpenDirectory(AppPaths.RootDirectory), "Папка данных открыта.");
        openConfig.Click += (_, _) => Run(() => ShellTools.OpenDirectory(AppPaths.ConfigDirectory), "Папка конфигурации открыта.");
        openLogs.Click += (_, _) => Run(() => ShellTools.OpenDirectory(AppPaths.LogDirectory), "Папка журналов открыта.");
        openLog.Click += (_, _) => Run(() => ShellTools.OpenFileOrParent(AppPaths.LogFile), "Журнал открыт.");
        github.Click += (_, _) => Run(() => ShellTools.OpenUrl(ProductInfo.RepositoryUrl), "Репозиторий открыт в браузере.");
        help.Click += (_, _) => openHelp();
        close.Click += (_, _) => Close();
        copyDiagnostics.Click += (_, _) => Run(() =>
        {
            System.Windows.Forms.Clipboard.SetText(ProductInfo.BuildDiagnosticSummary());
        }, "Диагностическая сводка скопирована без пользовательских данных.");

        var actionButtons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = double.NaN,
            Children = { openData, openConfig, openLogs, openLog, copyDiagnostics, help, github }
        };
        foreach (Control child in actionButtons.Children)
            child.Margin = new Thickness(0, 0, 8, 8);

        var content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                header,
                privacy,
                paths,
                Section("Действия"),
                actionButtons
            }
        };

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(20, 0, 20, 16)
        };
        footer.Children.Add(_status);
        Grid.SetColumn(close, 1);
        footer.Children.Add(close);

        var scroll = new ScrollViewer { Content = content };
        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children = { scroll, footer }
        };
        Grid.SetRow(footer, 1);
    }

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontSize = 17,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 4, 0, 0)
    };

    private static Control PathBlock(string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*") };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Opacity = 0.65,
            VerticalAlignment = VerticalAlignment.Center
        });

        var path = new TextBox
        {
            Text = value,
            IsReadOnly = true,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
            Padding = new Thickness(8, 5)
        };
        Grid.SetColumn(path, 1);
        grid.Children.Add(path);
        return grid;
    }

    private void Run(Action action, string success)
    {
        try
        {
            action();
            _status.Text = success;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Support UI action failed: {ex.GetType().Name}: {ex.Message}");
            _status.Text = $"Ошибка: {ex.Message}";
        }
    }
}
