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
        Width = 590;
        Height = 420;
        MinWidth = 520;
        MinHeight = 350;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = AppIcon.Get();

        _status = new TextBlock
        {
            Opacity = 0.56,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        var summary = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("110,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto")
        };
        AddRow(summary, 0, "Программа", "MacroEngine");
        AddRow(summary, 1, "Версия", ProductInfo.DisplayVersion, mono: true);
        AddRow(summary, 2, "Данные", AppPaths.IsPortable ? "Portable" : "LocalAppData");
        AddRow(summary, 3, "Диагностика", AppLog.DiagnosticEnabled ? "включена" : "выключена");

        var help = new Button { Content = "Справка", Classes = { "accent" } };
        var openData = new Button { Content = "Папка данных" };
        var copyDiagnostics = new Button { Content = "Копировать диагностику" };

        help.Click += (_, _) => openHelp();
        openData.Click += (_, _) => Run(
            () => ShellTools.OpenDirectory(AppPaths.RootDirectory),
            "Папка данных открыта.");
        copyDiagnostics.Click += (_, _) => Run(() =>
        {
            System.Windows.Forms.Clipboard.SetText(ProductInfo.BuildDiagnosticSummary());
        }, "Диагностика скопирована без пользовательских данных.");

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { help, openData, copyDiagnostics }
        };

        var details = BuildTechnicalDetails();
        details.IsVisible = false;
        var detailsButton = new Button
        {
            Content = "Техническая информация...",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(7, 3)
        };
        detailsButton.Click += (_, _) =>
        {
            details.IsVisible = !details.IsVisible;
            detailsButton.Content = details.IsVisible
                ? "Скрыть техническую информацию"
                : "Техническая информация...";
        };

        var content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 11,
            Children = { summary, actions, detailsButton, details }
        };

        var close = new Button { Content = "Закрыть", MinWidth = 84, IsCancel = true };
        close.Click += (_, _) => Close();

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(16, 0, 16, 12)
        };
        footer.Children.Add(_status);
        Grid.SetColumn(close, 1);
        footer.Children.Add(close);

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children = { new ScrollViewer { Content = content }, footer }
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

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { openConfig, openLogs, openLog, github }
        };

        return new StackPanel
        {
            Spacing = 6,
            Children =
            {
                PathRow("Программа", AppPaths.ApplicationDirectory),
                PathRow("Данные", AppPaths.RootDirectory),
                PathRow("Триггеры", AppPaths.TriggersFile),
                PathRow("Макросы", AppPaths.MacrosFile),
                PathRow("Журнал", AppPaths.LogFile),
                buttons,
                new TextBlock
                {
                    Text = AppLog.DiagnosticEnabled
                        ? "Диагностический журнал может содержать сведения о клавишах, раскладке и активных окнах."
                        : "Обычный журнал не содержит текст подстановок, команды, макросы и содержимое буфера обмена.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.54,
                    FontSize = 11,
                    Margin = new Thickness(0, 2, 0, 0)
                }
            }
        };
    }

    private static void AddRow(Grid grid, int row, string label, string value, bool mono = false)
    {
        var labelBlock = new TextBlock
        {
            Text = label,
            Opacity = 0.54,
            Margin = new Thickness(0, 3),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(labelBlock, row);
        grid.Children.Add(labelBlock);

        var valueBlock = new TextBlock
        {
            Text = value,
            Margin = new Thickness(0, 3),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = mono ? MonoFont : FontFamily.Default
        };
        Grid.SetRow(valueBlock, row);
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(valueBlock);
    }

    private static Control PathRow(string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("82,*") };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Opacity = 0.5,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11
        });

        var path = new TextBox
        {
            Text = value,
            IsReadOnly = true,
            FontFamily = MonoFont,
            FontSize = 11,
            Padding = new Thickness(5, 3)
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
