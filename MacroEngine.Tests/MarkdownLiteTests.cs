using MacroEngine.Core;
using Xunit;
using static MacroEngine.Core.MarkdownLite;

namespace MacroEngine.Tests;

public sealed class MarkdownLiteTests
{
    [Fact]
    public void ParsesBlocksAndInline()
    {
        var blocks = Parse("""
            # Title

            Intro with `code`, **bold** and [link](https://example.com).
            continues here.

            - first
              wrapped
            - second

            1. one
            2. two

            | A | B |
            |---|---|
            | `x` | a\|b |

            ```text
            raw **not bold**
            ```
            """);

        Assert.IsType<Heading>(blocks[0]);
        var p = Assert.IsType<Paragraph>(blocks[1]);
        Assert.Contains(p.Text, s => s.IsCode && s.Text == "code");
        Assert.Contains(p.Text, s => s.Bold && s.Text == "bold");
        Assert.Contains(p.Text, s => s.Url == "https://example.com" && s.Text == "link");
        Assert.EndsWith("continues here.", Plain(p.Text));

        var bullets = Assert.IsType<ListBlock>(blocks[2]);
        Assert.False(bullets.Ordered);
        Assert.Equal(new[] { "first wrapped", "second" }, bullets.Items.Select(Plain));

        var numbered = Assert.IsType<ListBlock>(blocks[3]);
        Assert.True(numbered.Ordered);
        Assert.Equal(2, numbered.Items.Count);

        var table = Assert.IsType<Table>(blocks[4]);
        Assert.Equal(new[] { "A", "B" }, table.Header.Select(Plain));
        Assert.Single(table.Rows);
        Assert.Equal("a|b", Plain(table.Rows[0][1]));

        var code = Assert.IsType<Code>(blocks[5]);
        Assert.Equal("raw **not bold**", code.Text);
    }

    [Fact]
    public void ReadmeSplitsIntoHelpSections()
    {
        var sections = Sections(Parse(ProductInfo.ReadEmbeddedReadme()), out string title);

        Assert.Equal("MacroEngine", title);
        var names = sections.Select(s => s.Title).ToList();
        Assert.Contains("Быстрый старт", names);
        Assert.Contains("Токены", names);
        Assert.Contains("Обновления", names);
        Assert.Contains("Безопасность выполнения", names);
        Assert.All(sections.Where(s => s.Title.Length > 0), s => Assert.NotEmpty(s.Blocks));

        var tokens = sections.Single(s => s.Title == "Токены").Blocks.OfType<Table>().Single();
        Assert.Contains(tokens.Rows, r => Plain(r[0]) == "{choice:Да|Нет|Не применимо}");
    }
}
