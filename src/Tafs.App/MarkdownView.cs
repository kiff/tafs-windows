using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Tafs.Core.Words;

namespace Tafs.App;

/// <summary>
/// A message's Markdown, drawn as text, as the iPad's <c>MarkdownText</c> (kiff/tafs#58, #211): paragraphs with their line
/// breaks, headings, bullet and numbered lists, code blocks, tables, and bold, italic, links and inline code within them.
/// No Markdown symbol shows as written; the words stay selectable.
/// </summary>
static class MarkdownView
{
    static Style Look(string key) => (Style)Application.Current.Resources[key];
    static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    /// <summary>The message's blocks, each its own line of text, a code block or a table.</summary>
    public static StackPanel Build(string markdown, double size, Func<string, Brush> brush)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var block in Markdown.Parse(markdown))
        {
            switch (block.Kind)
            {
                case MarkdownKind.Code:
                    var code = new TextBlock { Text = block.Plain, FontFamily = Mono, FontSize = size - 2, IsTextSelectionEnabled = true,
                        TextWrapping = TextWrapping.NoWrap, Style = Look("TafsInkText"), FlowDirection = FlowDirection.LeftToRight };
                    panel.Children.Add(new Border
                    {
                        Child = new ScrollViewer { Content = code, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                            HorizontalScrollMode = ScrollMode.Enabled },
                        Padding = new Thickness(12), CornerRadius = new CornerRadius(10), Background = brush("TafsRaised"),
                    });
                    break;
                case MarkdownKind.Table:
                    panel.Children.Add(Table(block.Rows!, size, brush));
                    break;
                default:
                    panel.Children.Add(Text(block, size, brush));
                    break;
            }
        }
        return panel;
    }

    static RichTextBlock Text(MarkdownBlock block, double size, Func<string, Brush> brush)
    {
        var text = new RichTextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, FontSize = block.Kind == MarkdownKind.Heading ? size + 1 : size,
            Foreground = brush("TafsInk") };
        var p = new Paragraph();
        if (block.Kind == MarkdownKind.Bullet) { p.Inlines.Add(new Run { Text = "•  " }); text.Margin = new Thickness(4, 0, 0, 0); }
        if (block.Kind == MarkdownKind.Numbered) { p.Inlines.Add(new Run { Text = block.Number + ".  " }); text.Margin = new Thickness(4, 0, 0, 0); }
        foreach (var span in block.Spans) p.Inlines.Add(Inline(span, block.Kind == MarkdownKind.Heading, size, brush));
        text.Blocks.Add(p);
        if (block.Kind == MarkdownKind.Heading) AutomationProperties.SetHeadingLevel(text, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level3);
        return text;
    }

    /// <summary>One run with its marks; a link (https, http or mailto, as the parser allows) opens in the browser.</summary>
    static Inline Inline(MarkdownSpan s, bool heading, double size, Func<string, Brush> brush)
    {
        var run = new Run { Text = s.Text };
        if (s.Bold || heading) run.FontWeight = FontWeights.SemiBold;
        if (s.Italic) run.FontStyle = Windows.UI.Text.FontStyle.Italic;
        if (s.Code) { run.FontFamily = Mono; run.FontSize = size - 1; }
        if (s.Link is { } link && Uri.TryCreate(link, UriKind.Absolute, out var uri))
        {
            var a = new Hyperlink { NavigateUri = uri, Foreground = brush("TafsCoach") };
            a.Inlines.Add(run);
            AutomationProperties.SetName(a, s.Text);
            return a;
        }
        return run;
    }

    /// <summary>A table, as the iPad's <c>MarkdownTable</c>: the header semibold, a line under it and between rows, numbers on the right; wider than the message, it scrolls sideways.</summary>
    static UIElement Table(IReadOnlyList<IReadOnlyList<IReadOnlyList<MarkdownSpan>>> rows, double size, Func<string, Brush> brush)
    {
        var grid = new Grid();
        var columns = rows.Max(r => r.Count);
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var c = 0; c < columns; c++)
            {
                var cell = new RichTextBlock { FontSize = size - 1, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.NoWrap, Foreground = brush("TafsInk"),
                    TextAlignment = Markdown.IsNumeric(c, rows) ? TextAlignment.Right : TextAlignment.Left };
                var p = new Paragraph();
                if (c < rows[r].Count) foreach (var span in rows[r][c]) p.Inlines.Add(Inline(span, r == 0, size - 1, brush));
                cell.Blocks.Add(p);
                var box = new Border { Child = cell, Padding = new Thickness(10, 6, 10, 6),
                    BorderBrush = brush("TafsLine"), BorderThickness = new Thickness(0, 0, 0, r < rows.Count - 1 ? (r == 0 ? 1 : 0.5) : 0) };
                Grid.SetRow(box, r);
                Grid.SetColumn(box, c);
                grid.Children.Add(box);
            }
        }
        var scroll = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled };
        AutomationProperties.SetName(scroll, Markdown.Spoken(rows));
        return scroll;
    }
}
