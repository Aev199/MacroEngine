using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using MacroEngine.Core;

namespace MacroEngine.UI;

/// <summary>Version and updates, where the data lives, privacy-safe diagnostics.</summary>
internal sealed class AboutWindow : Window
{
    private readonly UpdateCoordinator _updates;
    private readonly Snackbar _snack = new();

    private readonly TextBlock _updateStatus = new() { FontSize = UiTheme.FsBody, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _updateDetail = new() { FontSize = UiTheme.FsCaption, Foreground = UiTheme.B(UiTheme.TextGrey), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 4, MinHeight = 4, Margin = new Thickness(0, 8, 0, 0), IsVisible = false };
    private readonly Button _updateAction = UiTheme.Btn("", "primary");
    private readonly Button _notesToggle = UiTheme.Btn("Что нового", "link", small: true);
    private readonly Button _releasePage = UiTheme.Btn("Страница релиза", "link", small: true);
    private readonly Border _notes = new()
    {
        Background = UiTheme.B(UiTheme.SectionBg),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(14, 10),
        Margin = new Thickness(0, 10, 0, 0),
        MaxHeight = 220,
        IsVisible = false
    };
    private readonly CheckBox _autoCheck = new() { Content = "Проверять обновления раз в день", FontSize = UiTheme.FsLabel };

    public AboutWindow(UpdateCoordinator updates, Action openHelp)
    {
        _updates = updates;

        Title = "MacroEngine — О программе";
        Width = 600;
        Height = 620;
        MinWidth = 520;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = AppIcon.Get();

        var page = new StackPanel { Margin = new Thickness(24, 22, 24, 24) };
        page.Children.Add(BuildHeader(openHelp));
        page.Children.Add(UiTheme.SectionHeader("Обновления"));
        page.Children.Add(BuildUpdates());
        page.Children.Add(UiTheme.SectionHeader("Данные"));
        page.Children.Add(BuildData());

        Content = new Grid
        {
            Children =
            {
                new ScrollViewer { Content = page },
                _snack
            }
        };

        _updates.Changed += Refresh;
        Closed += (_, _) => _updates.Changed -= Refresh;
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) Close(); };
        Refresh();
    }

    /// <summary>Confirmation after the updater restarted into a new version.</summary>
    public void ShowUpdated() => _snack.Show($"Установлена версия {ProductInfo.DisplayVersion}", Tone.Success);

    private Control BuildHeader(Action openHelp)
    {
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        name.Children.Add(new TextBlock { Text = "MacroEngine", FontSize = UiTheme.FsTitle + 4, FontWeight = FontWeight.Bold });
        name.Children.Add(new TextBlock
        {
            Text = ProductInfo.Version,
            FontFamily = UiTheme.Mono,
            FontSize = UiTheme.FsBody,
            Foreground = UiTheme.B(UiTheme.TextMuted),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 3)
        });

        var help = UiTheme.Btn("Справка", "link", small: true);
        var github = UiTheme.Btn("GitHub", "link", small: true);
        help.Click += (_, _) => openHelp();
        github.Click += (_, _) => Run(() => ShellTools.OpenUrl(ProductInfo.RepositoryUrl));
        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-8, 4, 0, 0), Children = { help, github } };

        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                name,
                new TextBlock
                {
                    Text = "Подстановки текста, сочетания клавиш и макросы для Windows.",
                    FontSize = UiTheme.FsBody,
                    Foreground = UiTheme.B(UiTheme.TextMuted),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                },
                links
            }
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        row.Children.Add(new Image { Source = AppIcon.Image(), Width = 56, Height = 56, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 16, 0) });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private Control BuildUpdates()
    {
        _updateAction.Click += async (_, _) =>
        {
            switch (_updates.Stage)
            {
                case UpdateStage.Available:
                    await _updates.InstallAsync();
                    break;
                case UpdateStage.Downloading:
                    _updates.Cancel();
                    break;
                case UpdateStage.Installed:
                    if (!_updates.Restart())
                        _snack.Show("Закройте окно настроек, чтобы перезапустить MacroEngine", Tone.Warning);
                    break;
                default:
                    await _updates.CheckAsync();
                    if (_updates.Stage == UpdateStage.UpToDate) _snack.Show("Установлена последняя версия");
                    break;
            }
        };
        _notesToggle.Click += (_, _) =>
        {
            _notes.IsVisible = !_notes.IsVisible;
            _notesToggle.Content = _notes.IsVisible ? "Скрыть изменения" : "Что нового";
        };
        _releasePage.Click += (_, _) =>
        {
            if (_updates.Release?.PageUrl is { Length: > 0 } url) Run(() => ShellTools.OpenUrl(url));
        };
        _autoCheck.IsCheckedChanged += (_, _) => _updates.AutoCheck = _autoCheck.IsChecked == true;
        _autoCheck.Margin = new Thickness(0, 12, 0, 0);

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        top.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _updateStatus, _updateDetail } });
        _updateAction.VerticalAlignment = VerticalAlignment.Center;
        _updateAction.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(_updateAction, 1);
        top.Children.Add(_updateAction);

        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-8, 4, 0, 0), Children = { _notesToggle, _releasePage } };
        _notes.Child = new ScrollViewer();

        return new StackPanel { Children = { top, _progress, links, _notes, _autoCheck } };
    }

    private void Refresh()
    {
        ReleaseInfo? r = _updates.Release;
        string lastCheck = _updates.LastCheck is { } t ? $"Последняя проверка: {t.ToLocalTime():dd.MM.yyyy HH:mm}" : "Ещё не проверялось";
        (string status, string detail, string? action, Color color) = _updates.Stage switch
        {
            UpdateStage.Checking    => ("Проверка обновлений…", lastCheck, null, UiTheme.Text),
            UpdateStage.UpToDate    => ("Установлена последняя версия", lastCheck, "Проверить", UiTheme.Text),
            UpdateStage.Available   => ($"Доступна версия {r!.Version}", ReleaseDetail(r), $"Обновить до {r.Version}", UiTheme.Accent),
            UpdateStage.Downloading => ($"Загрузка {r!.Version}… {_updates.Progress:P0}", SizeText(r.PackageSize), "Отменить", UiTheme.Text),
            UpdateStage.Installed   => ($"Версия {r!.Version} установлена", "Перезапустите MacroEngine, чтобы перейти на неё.", "Перезапустить", UiTheme.Success),
            UpdateStage.Failed      => (_updates.Error ?? "Не удалось обновиться", lastCheck, "Повторить", UiTheme.Error),
            _                       => (_updates.LastCheck == null ? "Обновления ещё не проверялись" : "При последней проверке новых версий не было", _updates.LastCheck == null ? "" : lastCheck, "Проверить", UiTheme.Text)
        };

        _updateStatus.Text = status;
        _updateStatus.Foreground = UiTheme.B(color);
        _updateStatus.FontWeight = _updates.Stage is UpdateStage.Available or UpdateStage.Installed ? FontWeight.SemiBold : FontWeight.Normal;
        _updateDetail.Text = detail;
        _updateDetail.IsVisible = detail.Length > 0;
        _updateAction.IsVisible = action != null;
        _updateAction.Content = action;
        _updateAction.Classes.Set("primary", _updates.Stage is UpdateStage.Available or UpdateStage.Installed);
        _progress.IsVisible = _updates.Stage == UpdateStage.Downloading;
        _progress.Value = _updates.Progress;

        bool hasRelease = r != null && _updates.Stage is UpdateStage.Available or UpdateStage.Downloading or UpdateStage.Installed;
        _notesToggle.IsVisible = hasRelease && r!.Notes.Trim().Length > 0;
        _releasePage.IsVisible = hasRelease && r!.PageUrl.Length > 0;
        if (!_notesToggle.IsVisible) _notes.IsVisible = false;
        if (hasRelease && _notes.Child is ScrollViewer sv && sv.Tag as ReleaseInfo != r)
        {
            sv.Tag = r;
            sv.Content = MarkdownView.Render(MarkdownLite.Parse(r!.Notes));
        }
        _autoCheck.IsChecked = _updates.AutoCheck;
    }

    private static string ReleaseDetail(ReleaseInfo r)
    {
        string date = r.Published == default ? "" : $"от {r.Published.ToLocalTime():dd.MM.yyyy}";
        string kind = r.Prerelease ? "бета" : "";
        return string.Join(" · ", new[] { date, SizeText(r.PackageSize), kind }.Where(s => s.Length > 0));
    }

    private static string SizeText(long bytes) => bytes > 0 ? $"{bytes / 1024d / 1024d:0.#} МБ" : "";

    private Control BuildData()
    {
        var rows = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        int row = 0;
        void Path(string label, string path, Action open)
        {
            rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = UiTheme.Caption(label);
            l.Margin = new Thickness(0, 0, 14, 0);
            var value = new SelectableTextBlock
            {
                Text = path,
                FontFamily = UiTheme.Mono,
                FontSize = UiTheme.FsCaption,
                TextTrimming = TextTrimming.PathSegmentEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(value, path);
            var btn = UiTheme.Btn("Открыть", "link", small: true);
            btn.Click += (_, _) => Run(open);
            Grid.SetRow(l, row); Grid.SetRow(value, row); Grid.SetRow(btn, row);
            Grid.SetColumn(value, 1); Grid.SetColumn(btn, 2);
            rows.Children.Add(l); rows.Children.Add(value); rows.Children.Add(btn);
            row++;
        }
        Path("Настройки", AppPaths.RootDirectory, () => ShellTools.OpenDirectory(AppPaths.RootDirectory));
        Path("Журнал", AppPaths.LogFile, () => ShellTools.OpenFileOrParent(AppPaths.LogFile));
        Path("Программа", AppPaths.ApplicationDirectory, () => ShellTools.OpenDirectory(AppPaths.ApplicationDirectory));

        var mode = new TextBlock
        {
            Text = (AppPaths.IsPortable ? "Портативный режим: данные хранятся рядом с программой. " : "") +
                   (AppLog.DiagnosticEnabled
                       ? "Диагностическое логирование включено — журнал может содержать сведения о клавишах и окнах."
                       : "Журнал не содержит набранный текст, подстановки, команды и буфер обмена."),
            FontSize = UiTheme.FsCaption,
            Foreground = UiTheme.B(AppLog.DiagnosticEnabled ? UiTheme.Warning : UiTheme.TextGrey),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var copy = UiTheme.Btn("Копировать диагностику");
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        copy.Margin = new Thickness(0, 12, 0, 0);
        ToolTip.SetTip(copy, "Версия, ОС и пути к служебным файлам — без триггеров и их значений");
        copy.Click += async (_, _) =>
        {
            try
            {
                if (Clipboard != null)
                    await Clipboard.SetTextAsync(ProductInfo.BuildDiagnosticSummary());
                _snack.Show("Диагностика скопирована");
            }
            catch (Exception ex)
            {
                AppLog.Write($"Copy diagnostics failed: {ex.GetType().Name}: {ex.Message}");
                _snack.Show("Не удалось скопировать: " + ex.Message, Tone.Error);
            }
        };

        return new StackPanel { Children = { rows, mode, copy } };
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AppLog.Write($"About action failed: {ex.GetType().Name}: {ex.Message}");
            _snack.Show("Ошибка: " + ex.Message, Tone.Error);
        }
    }
}
