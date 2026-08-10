using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MacroEngine.Core;

namespace MacroEngine.UI;

/// <summary>Product information, support paths and privacy-safe diagnostics.</summary>
internal sealed class AboutWindow : Window
{
    private static readonly FontFamily MonoFont =
        new("Cascadia Mono,Consolas,monospace");

    private readonly TextBlock _status;

    public AboutWindow(Action openHelp)
    {
        Title = "MacroEngine — О программе";
        Width = 640;
        Height = 470;
        MinWidth = 540;
        MinHeight = 380;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = AppIcon.Get();

        _status = new TextBlock
        {
            Opacity = 0.6,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        var header = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock
                {
                    Text = "MacroEngine",
                    FontSize = 25,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = ProductInfo.DisplayVersion,
                    FontSize = 13,
                    FontFamily = MonoFont,
                    Opacity = 0.62
                },
                new TextBlock
                {
                    Text = "Локальная автоматизация Windows",
                    Opacity = 0.72,
                    Margin = new Thickness(0, 5, 0, 0)
                }
            }
        };

        var state = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("130,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Margin = new Thickness(0, 4, 0, 0)
        };
        state.Children.Add(Label("Данные"));
        var dataValue = Value(AppPaths.IsPortable ? "Portable" : "LocalAppData");
        Grid.SetColumn(dataValue, 1);
        state.Children.Add(dataValue);

        var diagLabel = Label("Диагностика");
        Grid.SetRow(diagLabel, 1);
        state.Children.Add(diagLabel);
        var diagValue = Value(AppLog.DiagnosticEnabled ? "включена" : "выключена");
        Grid.SetRow(diagValue, 1);
        Grid.SetColumn(diagValue, 1);
        state.Children.Add(diagValue);

        var openData = new Button { Content = "Папка данных" };
        var help = new Button { Content = "Справка", Classes = { "accent" } };
        var copyDiagnostics = new Button { Content = "Копировать диагностику" };
        openData.Click += (_, _) => Run(
            () => ShellTools.OpenDirectory(AppPaths.RootDirectory),
            "Папка данных открыта.");
        help.Click += (_, _) => openHelp();
        copyDiagnostics.Click += (_, _) => Run(() =>
        {
            System.Windows.Forms.Clipboard.SetText(ProductInfo.BuildDiagnosticSummary());
        }, "Диагностика скопирована без пользовательских данных.");

        var mainActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { help, openData, copyDiagnostics }
        };

        var details = BuildTechnicalDetails();
        details.IsVisible = false;

        var detailsButton = new Button
        {
            Content = "Техническая информация",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 4)
        };
        detailsButton.Click += (_, _) =>
        {
            details.IsVisible = !details.IsVisible;
            detailsButton.Content = details.IsVisible
                ? "Скрыть техническую информацию"
                : "Техническая информация";
        };

        var content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                header,
                state,
                mainActions,
                detailsButton,
                details
            }
        };

        var close = new Button { Content = "Закрыть", MinWidth = 88, IsCancel = true };
        close.Click += (_, _) => Close();

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

    private StackPanel BuildTechnicalDetails()
    {
        var openConfig = new Button { Content = "Конфиги" };
        var openLogs = new Button { Content = "Логи" };
        var openLog = new Button { Content = "Журнал" };
        var github = new Button { Content = "GitHub" };

        openConfig.Click += (_, _) => Run(
            () => ShellTools.OpenDirectory(AppPaths.ConfigDirectory),
            "Папка конфигурации открыта.");
        openLogs.Click += (_, _) => Run(
            () => ShellTools.OpenDirectory(AppPaths.LogDirectory),
            "Папка журналов открыта.");
        openLog.Click += (_, _) => Run(
            () => ShellTools.OpenFileOrParent(AppPaths.LogFile),
            "Журнал открыт.");
        github.Click += (_, _) => Run(
            () => ShellTools.OpenUrl(ProductInfo.RepositoryUrl),
            "Репозиторий открыт в браузере.");

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { openConfig, openLogs, openLog, github }
        };

        return new StackPanel
        {
            Spacing = 7,
            Children =
            {
                PathRow("Программа", AppPaths.ApplicationDirectory),
                PathRow("Данные", AppPaths.RootDirectory),
                PathRow("Триггеры", AppPaths.TriggersFile),
                PathRow("Макросы", AppPaths.MacrosFile),
                PathRow("Журнал", AppPaths.LogFile),
                actions,
                new TextBlock
                {
                    Text = AppLog.DiagnosticEnabled
                        ? "Диагностический журнал может содержать сведения о клавишах, раскладке и активных окнах."
                        : "Обычный журнал не содержит текст подстановок, команды, макросы и содержимое буфера обмена.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.58,
                    FontSize = 11,
                    Margin = new Thickness(0, 3, 0, 0)
                }
            }
        };
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Opacity = 0.56,
        Margin = new Thickness(0, 3)
    };

    private static TextBlock Value(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 3)
    };

    private static Control PathRow(string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*") };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Opacity = 0.52,
            VerticalAlignment = VerticalAlignment.Center
        });

        var path = new TextBox
        {
            Text = value,
            IsReadOnly = true,
            FontFamily = MonoFont,
            FontSize = 11,
            Padding = new Thickness(6, 4)
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
