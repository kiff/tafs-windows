using System.Text;
using System.Text.RegularExpressions;

namespace Tafs.Core.Words;

/// <summary>A run of a line's text with its inline marks: bold, italic, code, or a link's address.</summary>
/// <param name="Note">Where a link really goes, drawn dim after its words when they don't say it (agentgabo's read): never a link itself.</param>
public sealed record MarkdownSpan(string Text, bool Bold = false, bool Italic = false, bool Code = false, string? Link = null, bool Note = false);

public enum MarkdownKind { Paragraph, Heading, Bullet, Numbered, Code, Table }

/// <summary>
/// One block of a message, as the iPad's <c>MarkdownBlock</c>: its kind, its inline runs (code keeps its text as written), a
/// numbered item's number, and a table's rows (the header first), each cell with its runs.
/// </summary>
public sealed record MarkdownBlock(MarkdownKind Kind, IReadOnlyList<MarkdownSpan> Spans, int Number = 0, IReadOnlyList<IReadOnlyList<IReadOnlyList<MarkdownSpan>>>? Rows = null)
{
    /// <summary>The block's text with no mark, as it's read aloud.</summary>
    public string Plain => string.Concat(Spans.Select(s => s.Text));
}

/// <summary>
/// A message's Markdown, read as the iPad's <c>MarkdownText</c> (kiff/tafs#58, #211): paragraphs with their line breaks,
/// headings, bullet and numbered lists, code blocks and tables, and bold, italic, links and inline code within them. No
/// Markdown symbol shows as written; what isn't Markdown is the text as written.
/// </summary>
public static partial class Markdown
{
    /// <summary>Splits Markdown into blocks, line by line: consecutive plain lines are one paragraph, a blank line ends it.</summary>
    public static IReadOnlyList<MarkdownBlock> Parse(string source)
    {
        var output = new List<MarkdownBlock>();
        var paragraph = new List<string>();
        List<string>? code = null;
        var table = new List<string>();

        void FlushTable()
        {
            // A header, its |---| line and the rows; anything less is text, as written.
            if (table.Count >= 2 && IsTableRule(table[1]))
            {
                var rows = new[] { table[0] }.Concat(table.Skip(2))
                    .Select(r => (IReadOnlyList<IReadOnlyList<MarkdownSpan>>)Cells(r).Select(c => (IReadOnlyList<MarkdownSpan>)Inline(c)).ToList()).ToList();
                var plain = string.Join("\n", rows.Select(r => string.Join("\t", r.Select(c => string.Concat(c.Select(s => s.Text))))));
                output.Add(new(MarkdownKind.Table, [new(plain)], Rows: rows));
            }
            else paragraph.AddRange(table);
            table.Clear();
        }
        void Flush()
        {
            FlushTable();
            if (paragraph.Count > 0) output.Add(new(MarkdownKind.Paragraph, Inline(string.Join("\n", paragraph))));
            paragraph.Clear();
        }

        foreach (var raw in source.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim(' ', '\t');
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (code is not null)
                {
                    output.Add(new(MarkdownKind.Code, [new(string.Join("\n", code))]));
                    code = null;
                }
                else
                {
                    Flush();
                    code = [];
                }
                continue;
            }
            if (code is not null) { code.Add(raw); continue; }
            if (line.Length == 0) { Flush(); continue; }
            // A table's lines go together: a line that starts one (a pipe first, two cells or more) ends the paragraph before it.
            if (line.Contains('|') && (table.Count > 0 || (line.StartsWith('|') && Cells(line).Count >= 2)))
            {
                if (table.Count == 0 && paragraph.Count > 0)
                {
                    output.Add(new(MarkdownKind.Paragraph, Inline(string.Join("\n", paragraph))));
                    paragraph.Clear();
                }
                table.Add(line);
                continue;
            }
            FlushTable();
            Match m;
            if ((m = HeadingLine().Match(line)).Success) { Flush(); output.Add(new(MarkdownKind.Heading, Inline(m.Groups[1].Value))); }
            else if ((m = BulletLine().Match(line)).Success) { Flush(); output.Add(new(MarkdownKind.Bullet, Inline(m.Groups[1].Value))); }
            else if ((m = NumberedLine().Match(line)).Success)
            {
                Flush();
                output.Add(new(MarkdownKind.Numbered, Inline(m.Groups[2].Value), int.TryParse(m.Groups[1].Value, out var n) ? n : 1));
            }
            else if (line.Length >= 3 && line.All(c => c is '-' or '*' or '_')) Flush(); // a rule: nothing to draw
            else paragraph.Add(line);
        }
        if (code is not null) output.Add(new(MarkdownKind.Code, [new(string.Join("\n", code))]));
        Flush();
        return output;
    }

    [GeneratedRegex(@"^#{1,6}\s+(.+)$")] private static partial Regex HeadingLine();
    [GeneratedRegex(@"^[-*•+]\s+(.+)$")] private static partial Regex BulletLine();
    [GeneratedRegex(@"^(\d{1,3})[.)]\s+(.+)$")] private static partial Regex NumberedLine();

    /// <summary>A table row's cells: the text between its pipes, without the outer ones, trimmed; <c>\|</c> stays in its cell.</summary>
    public static IReadOnlyList<string> Cells(string line)
    {
        var row = line.Trim();
        if (row.StartsWith('|')) row = row[1..];
        if (row.EndsWith('|') && !row.EndsWith("\\|", StringComparison.Ordinal)) row = row[..^1];
        const string marker = "\u001F";
        return row.Replace("\\|", marker).Split('|').Select(c => c.Replace(marker, "|").Trim()).ToList();
    }

    /// <summary>The line under a table's header: a run of dashes per column, colons allowed.</summary>
    public static bool IsTableRule(string line)
    {
        var cells = Cells(line);
        return cells.Count >= 2 && cells.All(c => c.Length > 0 && c.Contains('-') && c.All(ch => ch is '-' or ':'));
    }

    /// <summary>
    /// Bold (<c>**</c>, <c>__</c>), italic (<c>*</c>, <c>_</c> not inside a word), inline code and links (https, http and mailto
    /// only; any other address is its text), as the iPad's inline-only Markdown; <c>\</c> escapes a mark. Unclosed marks are text.
    /// </summary>
    public static IReadOnlyList<MarkdownSpan> Inline(string text)
    {
        var spans = new List<MarkdownSpan>();
        Walk(text, false, false, null, spans);
        return Merge(spans);
    }

    /// <summary>Reads <paramref name="text"/> with the marks it's inside (bold, italic, a link's address) into <paramref name="spans"/>.</summary>
    static void Walk(string text, bool bold, bool italic, string? link, List<MarkdownSpan> spans)
    {
        var buffer = new StringBuilder();
        void Flush()
        {
            if (buffer.Length > 0) spans.Add(new(buffer.ToString(), bold, italic, Link: link));
            buffer.Clear();
        }
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && @"\`*_[]()#+-.!|".Contains(text[i + 1]))
            {
                buffer.Append(text[i + 1]);
                i += 2;
                continue;
            }
            if (c == '`' && text.IndexOf('`', i + 1) is var close and > 0)
            {
                Flush();
                spans.Add(new(text[(i + 1)..close], bold, italic, Code: true, Link: link));
                i = close + 1;
                continue;
            }
            if (c == '[' && link is null && LinkAt(text, i) is { } found)
            {
                Flush();
                var first = spans.Count;
                Walk(found.Label, bold, italic, found.Url, spans);
                // A link whose words aren't its own host or address shows where it goes after them: a taf's words carry what
                // it read, so "your bank" must never open somewhere else unseen.
                if (found.Url is { } url && LinkNote(string.Concat(spans.Skip(first).Select(x => x.Text)), url) is { } note)
                    spans.Add(new(" (" + note + ")", Note: true));
                i = found.End;
                continue;
            }
            if ((c == '*' || c == '_') && i + 1 < text.Length && text[i + 1] == c)
            {
                var end = text.IndexOf(new string(c, 2), i + 2, StringComparison.Ordinal);
                if (end > i + 2 && !char.IsWhiteSpace(text[i + 2]) && !char.IsWhiteSpace(text[end - 1]))
                {
                    Flush();
                    Walk(text[(i + 2)..end], true, italic, link, spans);
                    i = end + 2;
                    continue;
                }
            }
            if ((c == '*' || c == '_') && (c == '*' || i == 0 || !char.IsLetterOrDigit(text[i - 1])))
            {
                var end = FindSingle(text, c, i + 1);
                if (end > i + 1 && !char.IsWhiteSpace(text[i + 1]) && !char.IsWhiteSpace(text[end - 1]))
                {
                    Flush();
                    Walk(text[(i + 1)..end], bold, true, link, spans);
                    i = end + 1;
                    continue;
                }
            }
            buffer.Append(c);
            i++;
        }
        Flush();
    }

    static int FindSingle(string text, char mark, int from)
    {
        for (var j = from; j < text.Length; j++)
        {
            if (text[j] != mark) continue;
            if (j + 1 < text.Length && text[j + 1] == mark) { j++; continue; }
            if (mark == '_' && j + 1 < text.Length && char.IsLetterOrDigit(text[j + 1])) continue;
            return j;
        }
        return -1;
    }

    sealed record LinkMatch(string Label, string? Url, int End);

    static LinkMatch? LinkAt(string text, int i)
    {
        var close = text.IndexOf(']', i + 1);
        if (close < 0 || close + 1 >= text.Length || text[close + 1] != '(') return null;
        // The address ends at its own closing bracket: brackets inside it come in pairs, as CommonMark reads them.
        var end = -1;
        for (int j = close + 2, depth = 0; j < text.Length; j++)
        {
            if (text[j] == '(') depth++;
            else if (text[j] == ')' && depth-- == 0) { end = j; break; }
        }
        if (end < 0) return null;
        var address = text[(close + 2)..end].Trim();
        // https and mailto only: a taf has no reason to hand the owner an unencrypted page, and anything else is its words.
        var url = Uri.TryCreate(address, UriKind.Absolute, out var u) && u.Scheme is "https" or "mailto" ? u.AbsoluteUri : null;
        return new(text[(i + 1)..close], url, end + 1);
    }

    /// <summary>
    /// Where a link goes, when its words don't say it: its host (a mail link's address); none when the words are that host, the
    /// address itself, or the address without its scheme.
    /// </summary>
    public static string? LinkNote(string label, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
        var host = u.Scheme == "mailto" ? u.UserInfo + "@" + u.Host : u.IdnHost;
        var said = label.Trim().TrimEnd('/');
        var plain = u.AbsoluteUri.TrimEnd('/');
        var bare = plain.Contains("://", StringComparison.Ordinal) ? plain[(plain.IndexOf("://", StringComparison.Ordinal) + 3)..] : plain.Replace("mailto:", "");
        return string.Equals(said, host, StringComparison.OrdinalIgnoreCase) || string.Equals(said, plain, StringComparison.OrdinalIgnoreCase)
            || string.Equals(said, bare, StringComparison.OrdinalIgnoreCase) || string.Equals(said, "www." + host, StringComparison.OrdinalIgnoreCase)
            || string.Equals("www." + said, host, StringComparison.OrdinalIgnoreCase) ? null : host;
    }

    /// <summary>Neighbouring runs with the same marks become one, so a line is drawn in as few runs as it needs.</summary>
    static IReadOnlyList<MarkdownSpan> Merge(List<MarkdownSpan> spans)
    {
        var merged = new List<MarkdownSpan>();
        foreach (var s in spans)
        {
            if (merged.Count > 0 && merged[^1] is var last && last with { Text = "" } == s with { Text = "" }) merged[^1] = last with { Text = last.Text + s.Text };
            else merged.Add(s);
        }
        return merged;
    }

    /// <summary>
    /// An answer still being written, with what it's in the middle of closed, as the iPad's <c>healed</c> (kiff/tafs#285): bold or
    /// inline code left open, a link whose address isn't in yet (its text shows), a mark just opened with nothing after it.
    /// An open code block needs nothing: it's drawn as code until it closes.
    /// </summary>
    public static string Healed(string text)
    {
        var lines = text.Split('\n').ToList();
        var fences = lines.Count(l => l.Trim().StartsWith("```", StringComparison.Ordinal));
        if (fences % 2 != 0 || lines.Count == 0) return text;
        var last = lines[^1];
        lines.RemoveAt(lines.Count - 1);
        last = OpenLinkAddress().Replace(last, "$1");
        last = OpenLinkLabel().Replace(last, "$1");
        while (last.Length > 0 && last[^1] is '*' or '_' or '`')
        {
            var end = last[^1];
            var run = 0;
            while (run < last.Length && last[last.Length - 1 - run] == end) run++;
            var before = last[..^run];
            if (before.Length > 0 && !char.IsWhiteSpace(before[^1])) break;
            last = before;
        }
        if ((last.Split("**").Length - 1) % 2 == 1) last += "**";
        if (last.Count(ch => ch == '`') % 2 == 1) last += "`";
        lines.Add(last);
        return string.Join("\n", lines);
    }

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*$")] private static partial Regex OpenLinkAddress();
    [GeneratedRegex(@"\[([^\]]*)$")] private static partial Regex OpenLinkLabel();

    /// <summary>A column whose cells below the header are all amounts or numbers: drawn on the right.</summary>
    public static bool IsNumeric(int column, IReadOnlyList<IReadOnlyList<IReadOnlyList<MarkdownSpan>>> rows)
    {
        var body = rows.Skip(1).Where(r => column < r.Count).Select(r => string.Concat(r[column].Select(s => s.Text))).Where(c => c.Length > 0).ToList();
        if (body.Count == 0) return false;
        return body.All(cell =>
        {
            // An amount may carry a currency code ("20.00 USD"); any other letter makes it text.
            var bare = CurrencyCode().Replace(cell, "");
            return bare.Any(char.IsDigit) && bare.All(ch => "0123456789.,-+%$€£¥ ".Contains(ch));
        });
    }

    [GeneratedRegex(@"\b[A-Z]{3}\b")] private static partial Regex CurrencyCode();

    /// <summary>The table read aloud, row by row: each cell after its column's header.</summary>
    public static string Spoken(IReadOnlyList<IReadOnlyList<IReadOnlyList<MarkdownSpan>>> rows)
    {
        if (rows.Count == 0) return "";
        string Cell(IReadOnlyList<MarkdownSpan> c) => string.Concat(c.Select(s => s.Text));
        var header = rows[0].Select(Cell).ToList();
        // A header with no rows yet, as a streamed answer can be for a moment: its columns.
        if (rows.Count == 1) return string.Join(", ", header.Where(h => h.Length > 0));
        return string.Join(". ", rows.Skip(1).Select(row => string.Join(", ", row.Select((cell, i) =>
            (i < header.Count && header[i].Length > 0 ? header[i] + ": " : "") + Cell(cell)))));
    }
}
