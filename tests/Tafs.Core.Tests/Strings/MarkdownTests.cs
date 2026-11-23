using Tafs.Core.Words;

namespace Tafs.Core.Tests.Strings;

/// <summary>A message's Markdown, as the iPad's MarkdownTableTests and WorkLogTests read it: blocks, inline marks, tables, healing.</summary>
[Part("strings")]
public class MarkdownTests
{
    static string Text(IEnumerable<MarkdownSpan> spans) => string.Concat(spans.Select(s => s.Text));
    static string[][] Texts(MarkdownBlock b) => b.Rows!.Select(r => r.Select(Text).ToArray()).ToArray();

    [Fact]
    public void A_table_is_one_block_with_its_header_first()
    {
        var blocks = Markdown.Parse("Today's refunds:\n| Order | Amount |\n|---|--:|\n| T-3002 | $20.00 |\n| T-3003 | $8.50 |\n\nAll within the Card.");
        Assert.Equal(new[] { MarkdownKind.Paragraph, MarkdownKind.Table, MarkdownKind.Paragraph }, blocks.Select(b => b.Kind));
        Assert.Equal(new[] { new[] { "Order", "Amount" }, new[] { "T-3002", "$20.00" }, new[] { "T-3003", "$8.50" } }, Texts(blocks[1]));
        Assert.True(Markdown.IsNumeric(1, blocks[1].Rows!));
        Assert.False(Markdown.IsNumeric(0, blocks[1].Rows!), "an order id isn't an amount");
        var totals = Markdown.Parse("| Total |\n|---|\n| 20.00 USD |\n| 8 EUR |")[0];
        Assert.Equal(MarkdownKind.Paragraph, totals.Kind);
        var two = Markdown.Parse("| Total | x |\n|---|---|\n| 20.00 USD | a |\n| 8 EUR | b |")[0];
        Assert.True(Markdown.IsNumeric(0, two.Rows!), "an amount may carry its currency code");
    }

    [Fact]
    public void Cells_keep_their_inline_markdown_and_escaped_pipes()
    {
        var b = Markdown.Parse("| Charge | Note |\n| :-- | :-- |\n| `ch_1` | **refunded** a \\| b |")[0];
        Assert.Equal(new[] { new[] { "Charge", "Note" }, new[] { "ch_1", "refunded a | b" } }, Texts(b));
        Assert.True(b.Rows![1][0][0].Code);
        Assert.True(b.Rows![1][1][0].Bold);
    }

    [Fact]
    public void Pipes_without_a_rule_line_are_text_and_a_bullet_with_a_pipe_stays_a_bullet()
    {
        var b = Markdown.Parse("| not a table |\nshe said yes | no");
        Assert.Equal(new[] { MarkdownKind.Paragraph }, b.Select(x => x.Kind));
        Assert.Equal("| not a table |\nshe said yes | no", b[0].Plain);
        Assert.Equal(new[] { MarkdownKind.Bullet }, Markdown.Parse("- refund | return").Select(x => x.Kind));
    }

    [Fact]
    public void A_table_reads_aloud_row_by_row()
    {
        var rows = Markdown.Parse("| Order | Amount |\n|---|---|\n| T-3002 | $20.00 |")[0].Rows!;
        Assert.Equal("Order: T-3002, Amount: $20.00", Markdown.Spoken(rows));
        Assert.Equal("Order, Amount", Markdown.Spoken([rows[0]]));
    }

    [Fact]
    public void Blocks_are_paragraphs_headings_lists_code_and_rules()
    {
        var b = Markdown.Parse("# Done\nFirst line\nsecond line\n\n- one\n* two\n3. three\n---\n```\nlet x = **1**\n```");
        Assert.Equal(new[] { MarkdownKind.Heading, MarkdownKind.Paragraph, MarkdownKind.Bullet, MarkdownKind.Bullet, MarkdownKind.Numbered, MarkdownKind.Code },
            b.Select(x => x.Kind));
        Assert.Equal("First line\nsecond line", b[1].Plain);
        Assert.Equal(3, b[4].Number);
        Assert.Equal("let x = **1**", b[5].Plain);
    }

    [Fact]
    public void Inline_marks_never_show_as_written()
    {
        var s = Markdown.Inline("Refunded **$20** to *Ana* with `ch_1`, see [the order](https://shop.test/o/1) or [this](javascript:alert(1)).");
        Assert.Equal("Refunded $20 to Ana with ch_1, see the order (shop.test) or this.", Text(s));
        Assert.Contains(s, x => x is { Text: "$20", Bold: true });
        Assert.Contains(s, x => x is { Text: "Ana", Italic: true });
        Assert.Contains(s, x => x is { Text: "ch_1", Code: true });
        Assert.Contains(s, x => x is { Text: "the order", Link: "https://shop.test/o/1" });
        Assert.DoesNotContain(s, x => x.Link is { } l && l.StartsWith("javascript", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("snake_case_name stays", "snake_case_name stays")]
    [InlineData("2 * 3 * 4", "2 * 3 * 4")]
    [InlineData("an ** open mark", "an ** open mark")]
    [InlineData(@"a \*literal\* star", "a *literal* star")]
    [InlineData("__bold__ and _it_", "bold and it")]
    public void What_isn_t_markdown_is_the_text_as_written(string source, string shown) => Assert.Equal(shown, Text(Markdown.Inline(source)));

    [Theory]
    [InlineData("Refunded **$20", "Refunded **$20**")]
    [InlineData("Run `refund", "Run `refund`")]
    [InlineData("See [the order](https://shop.te", "See the order")]
    [InlineData("See [the ord", "See the ord")]
    [InlineData("Refunded **", "Refunded ")]
    [InlineData("**Done**", "**Done**")]
    [InlineData("**a**\nnext", "**a**\nnext")]
    [InlineData("```\nlet x = `", "```\nlet x = `")]
    public void An_answer_being_written_is_healed(string text, string healed) => Assert.Equal(healed, Markdown.Healed(text));

    [Theory]
    [InlineData("Refunded **$20")]
    [InlineData("See [the order](https://shop.te")]
    [InlineData("Run `refund")]
    public void No_symbol_shows_while_it_streams(string text)
    {
        var drawn = string.Concat(Markdown.Parse(Markdown.Healed(text)).Select(b => b.Plain));
        Assert.DoesNotContain('*', drawn);
        Assert.DoesNotContain('`', drawn);
        Assert.DoesNotContain('[', drawn);
    }

    /// <summary>A link's words that aren't where it goes show its host after them; its own host or address shows nothing more.</summary>
    [Theory]
    [InlineData("[https://kiff.dev](https://evil.example)", "https://kiff.dev (evil.example)")]
    [InlineData("[your bank](https://evil.example/login)", "your bank (evil.example)")]
    [InlineData("[kiff.dev](https://kiff.dev)", "kiff.dev")]
    [InlineData("[https://kiff.dev/](https://kiff.dev/)", "https://kiff.dev/")]
    [InlineData("[shop.test/o/1](https://shop.test/o/1)", "shop.test/o/1")]
    [InlineData("[write to us](mailto:help@kiff.dev)", "write to us (help@kiff.dev)")]
    [InlineData("[an old page](http://shop.test)", "an old page")]
    public void A_link_shows_where_it_goes_when_its_words_don_t(string source, string shown)
    {
        var spans = Markdown.Inline(source);
        Assert.Equal(shown, Text(spans));
        Assert.All(spans.Where(s => s.Note), s => Assert.Null(s.Link));
        if (source.Contains("http://", StringComparison.Ordinal)) Assert.All(spans, s => Assert.Null(s.Link));
    }
}
