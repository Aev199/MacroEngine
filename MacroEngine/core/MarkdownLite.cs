using System.Text;
using System.Text.RegularExpressions;

namespace MacroEngine.Core;

/// <summary>
/// The small Markdown subset README.md uses, parsed for the in-app help:
/// headings, paragraphs, bullet and numbered lists, pipe tables, fenced code,
/// and inline `code`, **bold** and [links](https://…). Anything else is plain text.
/// </summary>
internal static partial class MarkdownLite
{
    internal abstract record Block;
    internal sealed record Heading(int Level, IReadOnlyList<Span> Text) : Block;
    internal sealed record Paragraph(IReadOnlyList<Span> Text) : Block;
    internal sealed record ListBlock(bool Ordered, IReadOnlyList<IReadOnlyList<Span>> Items) : Block;
    internal sealed record Table(IReadOnlyList<IReadOnlyList<Span>> Header, IReadOnlyList<IReadOnlyList<IReadOnlyList<Span>>> Rows) : Block;
    internal sealed record Code(string Text) : Block;

    internal sealed record Span(string Text, bool Bold = false, bool IsCode = false, string? Url = null);

    /// <summary>A "## " chapter of the document with its blocks (the heading itself excluded).</summary>
    internal sealed record Section(string Title, IReadOnlyList<Block> Blocks, string PlainText);

    public static List<Block> Parse(string markdown)
    {
        var blocks = new List<Block>();
        string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
        var paragraph = new List<string>();
        int i = 0;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new Paragraph(Inline(string.Join(" ", paragraph.Select(l => l.Trim())))));
            paragraph.Clear();
        }

        while (i < lines.Length)
        {
            string line = lines[i];
            string trimmed = line.Trim();

            if (trimmed.StartsWith("```"))
            {
                FlushParagraph();
                var code = new StringBuilder();
                i++;
                while (i < lines.Length && !lines[i].Trim().StartsWith("```"))
                    code.AppendLine(lines[i++]);
                blocks.Add(new Code(code.ToString().TrimEnd('\n', '\r')));
                i++;
                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                i++;
                continue;
            }

            Match h = HeadingRx().Match(trimmed);
            if (h.Success)
            {
                FlushParagraph();
                blocks.Add(new Heading(h.Groups[1].Length, Inline(h.Groups[2].Value.Trim())));
                i++;
                continue;
            }

            if (trimmed.StartsWith('|'))
            {
                FlushParagraph();
                var rows = new List<IReadOnlyList<IReadOnlyList<Span>>>();
                while (i < lines.Length && lines[i].Trim().StartsWith('|'))
                {
                    string row = lines[i++].Trim();
                    if (TableRuleRx().IsMatch(row)) continue;
                    // "\|" is a literal pipe inside a cell (GitHub syntax).
                    rows.Add(row.Trim('|').Replace("\\|", "\u0001").Split('|')
                        .Select(c => (IReadOnlyList<Span>)Inline(c.Trim().Replace('\u0001', '|'))).ToList());
                }
                if (rows.Count > 0) blocks.Add(new Table(rows[0], rows.Skip(1).ToList()));
                continue;
            }

            Match item = ListItemRx().Match(line);
            if (item.Success)
            {
                FlushParagraph();
                bool ordered = char.IsDigit(item.Groups[1].Value[0]);
                var items = new List<IReadOnlyList<Span>>();
                var current = new StringBuilder(item.Groups[2].Value.Trim());
                i++;
                while (i < lines.Length)
                {
                    string next = lines[i];
                    Match nextItem = ListItemRx().Match(next);
                    if (nextItem.Success && char.IsDigit(nextItem.Groups[1].Value[0]) == ordered)
                    {
                        items.Add(Inline(current.ToString()));
                        current.Clear().Append(nextItem.Groups[2].Value.Trim());
                    }
                    else if (next.Length > 0 && char.IsWhiteSpace(next[0]) && next.Trim().Length > 0)
                    {
                        current.Append(' ').Append(next.Trim()); // continuation line
                    }
                    else break;
                    i++;
                }
                items.Add(Inline(current.ToString()));
                blocks.Add(new ListBlock(ordered, items));
                continue;
            }

            paragraph.Add(line);
            i++;
        }
        FlushParagraph();
        return blocks;
    }

    /// <summary>Split into "## " chapters; content before the first one becomes an untitled intro.</summary>
    public static List<Section> Sections(IReadOnlyList<Block> blocks, out string title)
    {
        title = "";
        var sections = new List<Section>();
        string? current = null;
        var body = new List<Block>();

        void Flush()
        {
            if (current == null && body.Count == 0) return;
            sections.Add(new Section(current ?? "", body.ToList(), PlainText(body)));
            body.Clear();
        }

        foreach (Block b in blocks)
        {
            if (b is Heading { Level: 1 } h1 && title.Length == 0) { title = Plain(h1.Text); continue; }
            if (b is Heading { Level: 2 } h2)
            {
                Flush();
                current = Plain(h2.Text);
                continue;
            }
            body.Add(b);
        }
        Flush();
        return sections;
    }

    public static string Plain(IEnumerable<Span> spans) => string.Concat(spans.Select(s => s.Text));

    private static string PlainText(IEnumerable<Block> blocks)
    {
        var sb = new StringBuilder();
        foreach (Block b in blocks)
        {
            switch (b)
            {
                case Heading h: sb.AppendLine(Plain(h.Text)); break;
                case Paragraph p: sb.AppendLine(Plain(p.Text)); break;
                case ListBlock l: foreach (var it in l.Items) sb.AppendLine(Plain(it)); break;
                case Table t:
                    sb.AppendLine(string.Join(" ", t.Header.Select(Plain)));
                    foreach (var r in t.Rows) sb.AppendLine(string.Join(" ", r.Select(Plain)));
                    break;
                case Code c: sb.AppendLine(c.Text); break;
            }
        }
        return sb.ToString();
    }

    public static List<Span> Inline(string text)
    {
        var spans = new List<Span>();
        int last = 0;
        foreach (Match m in InlineRx().Matches(text))
        {
            if (m.Index > last) spans.Add(new Span(text[last..m.Index]));
            if (m.Groups["code"].Success) spans.Add(new Span(m.Groups["code"].Value, IsCode: true));
            else if (m.Groups["bold"].Success) spans.Add(new Span(m.Groups["bold"].Value, Bold: true));
            else spans.Add(new Span(m.Groups["label"].Value, Url: m.Groups["url"].Value));
            last = m.Index + m.Length;
        }
        if (last < text.Length) spans.Add(new Span(text[last..]));
        return spans;
    }

    [GeneratedRegex(@"^(#{1,4})\s+(.*)$")]
    private static partial Regex HeadingRx();

    [GeneratedRegex(@"^\s{0,3}([-*]|\d+\.)\s+(.*)$")]
    private static partial Regex ListItemRx();

    [GeneratedRegex(@"^\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?$")]
    private static partial Regex TableRuleRx();

    [GeneratedRegex(@"`(?<code>[^`]+)`|\*\*(?<bold>.+?)\*\*|\[(?<label>[^\]]+)\]\((?<url>https?://[^)\s]+)\)")]
    private static partial Regex InlineRx();
}
