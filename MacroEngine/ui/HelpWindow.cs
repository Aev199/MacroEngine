using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using MacroEngine.Core;
using static MacroEngine.Core.MarkdownLite;

namespace MacroEngine.UI;

/// <summary>
/// Offline help: README.md embedded in the executable, one "##" chapter at a time,
/// with a chapter list and full-text search. README stays the single source, so the
/// help and the GitHub page never drift apart.
/// </summary>
internal sealed class HelpWindow : Window
{
    private const string IntroTitle = "О MacroEngine";

    private readonly List<Section> _sections;
    private readonly ListBox _nav = new();
    private readonly TextBox _search = new() { Watermark = "Поиск по справке" };
    private readonly ScrollViewer _content = new();
    private readonly TextBlock _empty = new()
    {
        Text = "Ничего не найдено",
        Foreground = UiTheme.B(UiTheme.TextGrey),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false
    };

    public HelpWindow(Action openAbout)
    {
        Title = "MacroEngine — Справка";
        Width = 940;
        Height = 660;
        MinWidth = 720;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = AppIcon.Get();

        _sections = Sections(Parse(ProductInfo.ReadEmbeddedReadme()), out _)
            .Select(s => s.Title.Length == 0 ? s with { Title = IntroTitle } : s)
            .ToList();

        _nav.Classes.Add("rows");
        _nav.ItemTemplate = new FuncDataTemplate<Section>((_, _) =>
        {
            var title = new TextBlock { FontSize = UiTheme.FsBody, TextTrimming = TextTrimming.CharacterEllipsis };
            title.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(Section.Title)));
            var bar = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left };
            bar.Classes.Add("selbar");
            return new Grid
            {
                Children = { new Border { Padding = new Thickness(14, 8, 10, 8), Child = title }, bar }
            };
        });
        _nav.SelectionChanged += (_, _) => Show(_nav.SelectedItem as Section);
        _search.TextChanged += (_, _) => Filter();

        var about = UiTheme.Btn("О программе", "link", small: true);
        about.Click += (_, _) => openAbout();
        var version = UiTheme.Caption(ProductInfo.DisplayVersion, UiTheme.TextGrey);
        version.Margin = new Thickness(6, 0, 0, 0);
        var footerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        footerRow.Children.Add(version);
        Grid.SetColumn(about, 1);
        footerRow.Children.Add(about);
        var footer = new Border
        {
            BorderBrush = UiTheme.B(UiTheme.Divider),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(8, 6),
            Child = footerRow
        };

        var left = new DockPanel();
        var searchBox = new Border { Padding = new Thickness(8), BorderBrush = UiTheme.B(UiTheme.Divider), BorderThickness = new Thickness(0, 0, 0, 1), Child = _search };
        DockPanel.SetDock(searchBox, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        left.Children.Add(searchBox);
        left.Children.Add(footer);
        left.Children.Add(_nav);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("250,8,*"), Margin = new Thickness(10) };
        body.Children.Add(UiTheme.Panel(left));
        var right = UiTheme.Panel(new Grid { Children = { _content, _empty } });
        Grid.SetColumn(right, 2);
        body.Children.Add(right);
        Content = body;

        Filter();
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape) Close();
            else if (e.Key == Avalonia.Input.Key.F && e.KeyModifiers == Avalonia.Input.KeyModifiers.Control) { _search.Focus(); _search.SelectAll(); }
        };
    }

    /// <summary>Open on a chapter by title (e.g. "Обновления"); unknown titles keep the first one.</summary>
    public void ShowSection(string title)
    {
        _search.Text = "";
        var section = _sections.FirstOrDefault(s => s.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
        if (section != null) _nav.SelectedItem = section;
    }

    private void Filter()
    {
        string q = (_search.Text ?? "").Trim();
        var visible = q.Length == 0
            ? _sections
            : _sections.Where(s => s.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                                   || s.PlainText.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        var selected = _nav.SelectedItem as Section;
        _nav.ItemsSource = visible;
        _nav.SelectedItem = selected != null && visible.Contains(selected) ? selected : visible.FirstOrDefault();
        _empty.IsVisible = visible.Count == 0;
        _content.IsVisible = visible.Count > 0;
    }

    private void Show(Section? section)
    {
        if (section == null) return;
        var page = new StackPanel { Margin = new Thickness(28, 20, 28, 28), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        page.Children.Add(new TextBlock
        {
            Text = section.Title,
            FontSize = UiTheme.FsTitle + 4,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 14)
        });
        page.Children.Add(MarkdownView.Render(section.Blocks));
        _content.Content = page;
        _content.Offset = default;
    }
}
