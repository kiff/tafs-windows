using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Tafs.Core.Record;
using Tafs.Core.Words;

namespace Tafs.App;

/// <summary>
/// A Record line's sheet, as the iPad's <c>ReceiptView</c>: what became of it, the facts, what happened as KIFF signed it,
/// and KIFF's receipt as it wrote it, with the full receipt and Copy. The check of KIFF's signature on this PC comes in its
/// own change: until then the receipt is shown, never said to be checked.
/// </summary>
sealed class ReceiptSheet
{
    readonly RecordEntry entry;
    readonly ElementTheme theme;
    readonly StackPanel body = new() { Spacing = 16 };
    readonly StackPanel happened = new() { Spacing = 0 };
    readonly Border receipt;
    byte[]? raw;

    /// <summary>How the sheet reads KIFF's receipt as it wrote it: its bytes, or why not. None signed out.</summary>
    public static Func<string, Task<(byte[]? Raw, string? Why)>>? Read { get; set; }

    ReceiptSheet(RecordEntry entry, ElementTheme theme)
    {
        this.entry = entry;
        this.theme = theme;
        receipt = Panel();
    }

    public static async Task ShowAsync(XamlRoot root, ElementTheme theme, RecordEntry entry)
    {
        var sheet = new ReceiptSheet(entry, theme);
        var dialog = new ContentDialog
        {
            XamlRoot = root, RequestedTheme = theme, CloseButtonText = App.Words.Get("record.filter.done"),
            Content = new ScrollViewer { Content = sheet.Draw(App.Words), MaxHeight = 640 },
        };
        _ = sheet.LoadAsync();
        await dialog.ShowAsync();
    }

    StackPanel Draw(WordBook w)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var colour = Brush(RecordScreen.DotOf(entry.Kind) switch { Dot.Went => "TafsOk", Dot.Waiting => "TafsAmber", _ => "TafsNo" });
        head.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = colour, VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(new TextBlock { Text = Receipt.StateLabel(w, entry), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = colour });
        body.Children.Add(head);
        body.Children.Add(Words(entry.Title, 30, "TafsInk", FontWeights.Bold));

        var facts = Panel();
        var lines = new StackPanel { Spacing = 8 };
        lines.Children.Add(Fact(w.Get("receipt.fact.taf"), entry.Taf));
        lines.Children.Add(Fact(w.Get("receipt.fact.at"), Receipt.Shown(w, entry.At, TimeZoneInfo.Local)));
        lines.Children.Add(Fact(w.Get("receipt.fact.outcome"), Receipt.Outcome(w, entry), bold: true));
        // KIFF's words, never its codes.
        if (entry.Call?.Reason is { } why) lines.Children.Add(Fact(w.Get("receipt.fact.why"), why));
        lines.Children.Add(Fact(w.Get("receipt.fact.decided_by"), Receipt.DecidedBy(w, entry)));
        facts.Child = lines;
        body.Children.Add(facts);
        body.Children.Add(happened);
        if (entry.Call?.Receipt is not null) body.Children.Add(receipt);
        return body;
    }

    /// <summary>KIFF's receipt, as the iPad's <c>loadReceipt</c>: read when the sheet opens; Try again when it couldn't be.</summary>
    async Task LoadAsync()
    {
        var w = App.Words;
        if (entry.Call?.Receipt is not { } r) return;
        if (Read is not { } read) return;
        receipt.Child = new ProgressRing { IsActive = true, Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left };
        var (bytes, why) = await read(Receipt.Path(r.TraceId));
        if (bytes is null)
        {
            var again = new Button { Content = w.Get("common.try_again"), Style = (Style)Application.Current.Resources["TafsOutlineButton"] };
            again.Click += (_, _) => _ = LoadAsync();
            receipt.Child = Stack(Words(w.Get("receipt.not_loaded", ("why", why ?? "")), 13, "TafsNo"), again);
            return;
        }
        raw = bytes;
        var steps = Receipt.Steps(bytes);
        if (steps.Count > 0) DrawSteps(w, steps);
        DrawReceipt(w, steps);
    }

    /// <summary>What happened, as the iPad's timeline: each step on a line of dots, its words capitalised, its time.</summary>
    void DrawSteps(WordBook w, IReadOnlyList<ReceiptStep> steps)
    {
        happened.Children.Add(new TextBlock { Text = w.Get("receipt.what_happened"), Style = (Style)Application.Current.Resources["TafsEyebrow"], Margin = new Thickness(0, 0, 0, 8) });
        for (var i = 0; i < steps.Count; i++)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var rail = new Grid();
            if (i < steps.Count - 1) rail.Children.Add(new Rectangle { Width = 2, Fill = Brush("TafsLine"), Margin = new Thickness(0, 10, 0, 0) });
            rail.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = Brush("TafsPanel"), Stroke = Brush("TafsDim"), StrokeThickness = 2,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0) });
            row.Children.Add(rail);
            var words = steps[i].Words;
            var text = Stack(Words(words[..1].ToUpper(System.Globalization.CultureInfo.CurrentCulture) + words[1..], 14, "TafsInk"));
            if (steps[i].At is { } at) text.Children.Add(Mono(Receipt.Shown(w, at, TimeZoneInfo.Local, withDate: false), 12));
            text.Margin = new Thickness(0, 0, 0, 12);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            happened.Children.Add(row);
        }
    }

    /// <summary>
    /// The receipt's panel: a line KIFF's steps contradict, else the receipt's decision and when it was issued, and Copy.
    /// Whether KIFF signed it, the full receipt with its signer, come with its check.
    /// </summary>
    void DrawReceipt(WordBook w, IReadOnlyList<ReceiptStep> steps)
    {
        var shown = new StackPanel { Spacing = 8 };
        if (Receipt.Mismatch(w, entry, steps) is { } wrong)
        {
            receipt.BorderBrush = Brush("TafsNo");
            shown.Children.Add(Words(w.Get("record.kiff_receipt_does_not_check"), 14, "TafsNo", FontWeights.SemiBold));
            shown.Children.Add(Words(wrong, 13, "TafsSoft"));
        }
        else shown.Children.Add(Words(w.Get("receipt.kiff_receipt"), 14, "TafsInk", FontWeights.SemiBold));
        var issued = Receipt.Field(raw!, "issued_at") is { } at && DateTimeOffset.TryParse(at, out var when)
            ? " · " + w.Get("receipt.issued", ("at", Receipt.Shown(w, when, TimeZoneInfo.Local))) : "";
        shown.Children.Add(Mono(Receipt.DecisionWords(w, entry) + issued, 11.5));
        var copy = new Button { Content = w.Get("receipt.copy"), Style = (Style)Application.Current.Resources["TafsOutlineButton"] };
        copy.Click += (_, _) =>
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(System.Text.Encoding.UTF8.GetString(raw!));
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        };
        shown.Children.Add(copy);
        receipt.Child = shown;
    }

    Grid Fact(string name, string value, bool bold = false)
    {
        var line = new Grid { ColumnSpacing = 8 };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.Children.Add(Words(name, 13, "TafsDim"));
        var text = Words(value, 14, "TafsInk", bold ? FontWeights.SemiBold : FontWeights.Normal);
        Grid.SetColumn(text, 1);
        line.Children.Add(text);
        return line;
    }

    Border Panel() => new()
    {
        Background = Brush("TafsPanel"), BorderBrush = Brush("TafsLine"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
        Padding = new Thickness(14),
    };

    static StackPanel Stack(params UIElement[] children)
    {
        var s = new StackPanel { Spacing = 6 };
        foreach (var c in children) s.Children.Add(c);
        return s;
    }

    TextBlock Words(string text, double size, string colour, Windows.UI.Text.FontWeight? weight = null) => new()
    {
        Text = text, FontSize = size, Foreground = Brush(colour), TextWrapping = TextWrapping.Wrap, FontWeight = weight ?? FontWeights.Normal,
        IsTextSelectionEnabled = true,
    };

    TextBlock Mono(string text, double size) => new()
    {
        Text = text, FontSize = size, FontFamily = new FontFamily("Cascadia Mono, Consolas"), Foreground = Brush("TafsDim"),
        TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
    };

    /// <summary>A theme brush for the sheet's theme, as the app's code takes them.</summary>
    Brush Brush(string key)
    {
        var name = theme == ElementTheme.Dark ? "Dark" : "Light";
        foreach (var merged in Application.Current.Resources.MergedDictionaries.Prepend(Application.Current.Resources))
            if (merged.ThemeDictionaries.TryGetValue(name, out var found) && found is ResourceDictionary d && d.TryGetValue(key, out var brush) && brush is Brush b)
                return b;
        return (Brush)Application.Current.Resources[key];
    }
}
