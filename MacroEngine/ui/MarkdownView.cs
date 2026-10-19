using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MacroEngine.Core;
using static MacroEngine.Core.MarkdownLite;
using Span = MacroEngine.Core.MarkdownLite.Span;

namespace MacroEngine.UI;

/// <summary>Renders <see cref="MarkdownLite"/> blocks with the GeoBase tokens (help, release notes).</summary>
internal static class MarkdownView
{
    public static Control Render(IEnumerable<Block> blocks)
    {
        var panel = new StackPanel { Spacing = 10 };
        foreach (Block block in blocks)
            panel.Children.Add(RenderBlock(block));
        return panel;
    }

    private static Control RenderBlock(Block block) => block switch
    {
        Heading h => Text(h.Text, h.Level <= 2 ? UiTheme.FsPanel : UiTheme.FsBody + 1, FontWeight.SemiBold, new Thickness(0, 6, 0, 0)),
        Paragraph p => Text(p.Text),
        ListBlock l => List(l),
        Table t => TableView(t),
        Code c => new Border
        {
            Background = UiTheme.B(UiTheme.SectionBg),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 9),
            Child = new SelectableTextBlock { Text = c.Text, FontFamily = UiTheme.Mono, FontSize = UiTheme.FsLabel, TextWrapping = TextWrapping.Wrap }
        },
        _ => new Control()
    };

    public static SelectableTextBlock Text(IReadOnlyList<Span> spans, double size = UiTheme.FsBody,
        FontWeight weight = FontWeight.Normal, Thickness? margin = null)
    {
        var tb = new SelectableTextBlock
        {
            FontSize = size,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = size * 1.5,
            Foreground = UiTheme.B(UiTheme.Text),
            Margin = margin ?? default
        };
        tb.Inlines ??= new InlineCollection();
        foreach (Span s in spans)
        {
            if (s.Url != null)
            {
                string url = s.Url;
                var link = new TextBlock
                {
                    Text = s.Text, FontSize = size, Foreground = UiTheme.B(UiTheme.Accent),
                    TextDecorations = TextDecorations.Underline, Cursor = new Cursor(StandardCursorType.Hand)
                };
                ToolTip.SetTip(link, url);
                link.PointerPressed += (_, _) =>
                {
                    try { ShellTools.OpenUrl(url); }
                    catch (Exception ex) { AppLog.Write($"Open link failed: {ex.GetType().Name}"); }
                };
                tb.Inlines.Add(new InlineUIContainer { Child = link, BaselineAlignment = BaselineAlignment.TextBottom });
            }
            else if (s.IsCode)
            {
                tb.Inlines.Add(new Run(s.Text)
                {
                    FontFamily = UiTheme.Mono,
                    FontSize = size - 1,
                    Background = UiTheme.B(UiTheme.SectionBg),
                    Foreground = UiTheme.B(UiTheme.Text)
                });
            }
            else
            {
                tb.Inlines.Add(new Run(s.Text) { FontWeight = s.Bold ? FontWeight.SemiBold : weight });
            }
        }
        return tb;
    }

    private static Control List(ListBlock list)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        for (int i = 0; i < list.Items.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var marker = new TextBlock
            {
                Text = list.Ordered ? $"{i + 1}." : "•",
                Foreground = UiTheme.B(UiTheme.TextMuted),
                Margin = new Thickness(4, 0, 10, 4),
                LineHeight = UiTheme.FsBody * 1.5,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var text = Text(list.Items[i]);
            text.Margin = new Thickness(0, 0, 0, 4);
            Grid.SetRow(marker, i);
            Grid.SetRow(text, i);
            Grid.SetColumn(text, 1);
            grid.Children.Add(marker);
            grid.Children.Add(text);
        }
        return grid;
    }

    private static Control TableView(Table table)
    {
        int columns = Math.Max(table.Header.Count, table.Rows.Select(r => r.Count).DefaultIfEmpty(0).Max());
        // The column with the most text takes the free width and wraps; the others
        // (names, keys, short examples) keep their natural width.
        int prose = Enumerable.Range(0, columns)
            .OrderByDescending(c => table.Rows.Sum(r => c < r.Count ? Plain(r[c]).Length : 0))
            .First();
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Range(0, columns).Select(c => c == prose ? "*" : "Auto")))
        };

        void AddRow(IReadOnlyList<IReadOnlyList<Span>> cells, int row, bool header)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int c = 0; c < columns; c++)
            {
                var content = c < cells.Count ? cells[c] : Array.Empty<Span>();
                var text = Text(content, header ? UiTheme.FsLabel : UiTheme.FsBody, header ? FontWeight.SemiBold : FontWeight.Normal);
                if (header) text.Foreground = UiTheme.B(UiTheme.TextMuted);
                var cell = new Border
                {
                    Background = header ? UiTheme.B(UiTheme.SectionBg) : Brushes.Transparent,
                    BorderBrush = UiTheme.B(UiTheme.Divider),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(10, 6, 16, 6),
                    Child = text
                };
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        AddRow(table.Header, 0, header: true);
        for (int r = 0; r < table.Rows.Count; r++)
            AddRow(table.Rows[r], r + 1, header: false);

        return new Border
        {
            BorderBrush = UiTheme.B(UiTheme.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = grid
        };
    }
}
