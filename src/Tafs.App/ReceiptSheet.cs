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
/// and KIFF's receipt as it wrote it, with the full receipt and Copy. KIFF's signature is checked on this PC
/// (<see cref="ReceiptCheck"/>): what happened is drawn only once it checks out and matches the line.
/// </summary>
sealed class ReceiptSheet
{
    readonly RecordEntry entry;
    readonly ElementTheme theme;
    readonly StackPanel body = new() { Spacing = 16 };
    readonly StackPanel happened = new() { Spacing = 0 };
    readonly Border receipt;
    readonly ScrollViewer scroller = new() { MaxHeight = 640 };
    /// <summary>"Report this payment", while tafs cloud offers it; gone once it's reported from here.</summary>
    readonly Button report = new();
    byte[]? raw;
    bool sending;

    /// <summary>This PC's check of the receipt; none while it's read.</summary>
    ReceiptCheck? check;

    /// <summary>How the sheet reads KIFF's receipt as it wrote it: its bytes, or why not. None signed out.</summary>
    public static Func<string, Task<(byte[]? Raw, string? Why)>>? Read { get; set; }

    /// <summary>How the sheet reports a payment to tafs cloud (<c>POST /v1/record/{trace_id}/report</c>): whether it took it. None signed out.</summary>
    public static Func<string, Task<bool>>? SendReport { get; set; }

    /// <summary>
    /// The pictures a paired Chrome kept before this operation's steps (planque's safe route, signed by this PC's device key), by
    /// KIFF's <c>proposal_id</c>; none on any failure, so the receipt shows no link. None signed out.
    /// </summary>
    public static Func<string, Task<IReadOnlyList<Tafs.Core.Kiff.ShotRef>>>? Shots { get; set; }

    /// <summary>One step's picture, read and checked (<see cref="Tafs.Core.Kiff.ChromeShots.Read"/>). None signed out.</summary>
    public static Func<string, Task<(Tafs.Core.Kiff.ShotRead Read, Tafs.Core.Kiff.ChromeShot? Shot)>>? Shot { get; set; }

    /// <summary>"See the page before this step", one a picture planque kept for the line's operation; empty until read.</summary>
    readonly StackPanel shots = new() { Spacing = 8 };

    /// <summary>A payment was reported from a receipt: its taf is halted, so Home is read again.</summary>
    public static event Action? Reported;

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
            Content = sheet.scroller,
        };
        sheet.scroller.Content = sheet.Draw(App.Words);
        _ = sheet.LoadAsync();
        await dialog.ShowAsync();
    }

    StackPanel Draw(WordBook w)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var colour = Brush(RecordScreen.DotOf(entry.Kind) switch { Dot.Went => "TafsOk", Dot.Waiting => "TafsAmber", Dot.Quiet => "TafsDim", _ => "TafsNo" });
        head.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = colour, VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(new TextBlock { Text = Receipt.StateLabel(w, entry), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = colour });
        body.Children.Add(head);
        body.Children.Add(Words(entry.Title, 30, "TafsInk", FontWeights.Bold));

        var facts = Panel();
        var lines = new StackPanel { Spacing = 8 };
        lines.Children.Add(Fact(w.Get("receipt.fact.taf"), entry.Taf));
        lines.Children.Add(Fact(w.Get("receipt.fact.at"), Receipt.Shown(w, entry.At, TimeZoneInfo.Local)));
        // What it acted on, when tafs knows the value and it's what KIFF hashed (kiff/tafs#167).
        if (entry.OnValue is { } on) lines.Children.Add(Fact(w.Get("receipt.fact.on"), ((entry.Call?.OnArg ?? "").Replace('_', ' ') + " " + on).Trim()));
        lines.Children.Add(Fact(w.Get("receipt.fact.outcome"), Receipt.Outcome(w, entry), bold: true));
        // KIFF's words, never its codes.
        if (entry.Call?.Reason is { } why) lines.Children.Add(Fact(w.Get("receipt.fact.why"), why));
        lines.Children.Add(Fact(w.Get("receipt.fact.decided_by"), Receipt.DecidedBy(w, entry)));
        facts.Child = lines;
        body.Children.Add(facts);
        body.Children.Add(happened);
        if (entry.Call?.Receipt is not null) body.Children.Add(receipt);
        // The pictures a paired Chrome kept before this operation's steps (the iPad's ChromeShotView): a link each, once read.
        body.Children.Add(shots);
        if (entry.Call?.ProposalId is { Length: > 0 } operation && Shots is { } list) _ = LoadShotsAsync(w, operation, list);
        // Where tafs cloud offers it, until it's reported: outlined in red, as the iPad's.
        if (ReportPayment.Offers(entry) && SendReport is not null)
        {
            report.Content = w.Get("record.report.button");
            report.Style = (Style)Application.Current.Resources["TafsOutlineButton"];
            report.Foreground = Brush("TafsNo");
            report.BorderBrush = Brush("TafsNo");
            report.HorizontalAlignment = HorizontalAlignment.Stretch;
            report.Click += (_, _) => ShowReport(w, ReportPhase.Asking);
            body.Children.Add(report);
        }
        return body;
    }

    /// <summary>The links, one per picture planque kept for the operation, oldest first; none on any failure.</summary>
    async Task LoadShotsAsync(WordBook w, string operation, Func<string, Task<IReadOnlyList<Tafs.Core.Kiff.ShotRef>>> list)
    {
        IReadOnlyList<Tafs.Core.Kiff.ShotRef> kept;
        // Unawaited: any failure (offline, no device key to sign with) is no link, never an escape.
        try { kept = await list(operation); }
        catch (Exception) { return; }
        foreach (var shot in kept)
        {
            var open = Wide(w.Get("chrome.shot_open"), "TafsOutlineButton", () => ShowShot(w, shot.Step));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(open, "record.chromeShot");
            shots.Children.Add(open);
        }
    }

    /// <summary>
    /// The picture of a step (the iPad's <c>ChromeShotView</c>), in this sheet's place: "Before this step", the picture, when it
    /// was taken and that it's kept for 30 days; or that none is kept; or that it couldn't be loaded, never a blank. Back to the
    /// receipt with Done.
    /// </summary>
    void ShowShot(WordBook w, string step)
    {
        var view = new StackPanel { Spacing = 12 };
        view.Children.Add(Words(w.Get("chrome.shot_title"), 26, "TafsInk", FontWeights.Bold));
        var place = new StackPanel { Spacing = 10 };
        place.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left });
        view.Children.Add(place);
        view.Children.Add(Wide(w.Get("common.done"), "TafsOutlineButton", () => scroller.Content = body));
        scroller.Content = view;
        _ = LoadShotAsync(w, step, place);
    }

    async Task LoadShotAsync(WordBook w, string step, StackPanel place)
    {
        (Tafs.Core.Kiff.ShotRead Read, Tafs.Core.Kiff.ChromeShot? Shot) read = (Tafs.Core.Kiff.ShotRead.Failed, null);
        // Unawaited: any failure reads as "The picture couldn't be loaded".
        try { if (Shot is { } get) read = await get(step); }
        catch (Exception) { }
        place.Children.Clear();
        if (read is { Read: Tafs.Core.Kiff.ShotRead.Shown, Shot: { } shot })
        {
            var image = new Image { Stretch = Stretch.Uniform, MaxHeight = 360, HorizontalAlignment = HorizontalAlignment.Left };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(image, w.Get("chrome.shot_title"));
            if (await Drawn(image, shot.Image))
            {
                place.Children.Add(new Border { Child = image, BorderBrush = Brush("TafsLine"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                    HorizontalAlignment = HorizontalAlignment.Left });
                place.Children.Add(Words(Receipt.Shown(w, shot.TakenAt, TimeZoneInfo.Local), 13, "TafsDim"));
                place.Children.Add(Words(w.Get("chrome.shot_kept"), 13, "TafsDim"));
                return;
            }
        }
        place.Children.Add(read.Read == Tafs.Core.Kiff.ShotRead.None
            ? Words(w.Get("chrome.shot_none"), 15, "TafsDim")
            : Words(w.Get("chrome.shot_failed"), 15, "TafsNo"));
    }

    /// <summary>The picture decoded on this PC from its checked bytes; one Windows can't decode is no picture.</summary>
    static async Task<bool> Drawn(Image image, byte[] bytes)
    {
        try
        {
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await stream.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(bytes));
            stream.Seek(0);
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            await bitmap.SetSourceAsync(stream);
            image.Source = bitmap;
            return true;
        }
        catch (Exception) { return false; }
    }

    enum ReportPhase { Asking, Sending, Done, Failed }

    /// <summary>
    /// "Report this payment", as the iPad's <c>ReportPaymentSheet</c>, in this sheet's place (Windows shows one dialog at a time):
    /// the title; what it reports and that the taf halts, with Report and halt {taf} and Cancel; once tafs cloud takes it,
    /// Reported, with Done back to the receipt, its button gone. If it doesn't go through, it says so and the button stays:
    /// sending again is safe. No payee, no note and no reason: tafs keeps none.
    /// </summary>
    void ShowReport(WordBook w, ReportPhase phase)
    {
        var view = new StackPanel { Spacing = 16 };
        view.Children.Add(Words(w.Get("record.report.title"), 30, "TafsInk", FontWeights.Bold));
        if (phase == ReportPhase.Done)
        {
            view.Children.Add(Words(w.Get("record.report.done", ("taf", entry.Taf)), 16, "TafsInk"));
            view.Children.Add(Wide(w.Get("common.done"), "TafsPrimary", () => scroller.Content = body));
        }
        else
        {
            view.Children.Add(Words(ReportPayment.Intro(w, entry, TimeZoneInfo.Local), 16, "TafsInk"));
            if (phase == ReportPhase.Failed) view.Children.Add(Words(w.Get("record.report.failed"), 14, "TafsNo", FontWeights.SemiBold));
            var send = Wide(w.Get("record.report.submit", ("taf", entry.Taf)), "TafsPrimary", () => _ = SendAsync(w));
            send.IsEnabled = phase != ReportPhase.Sending;
            view.Children.Add(send);
            view.Children.Add(Wide(w.Get("common.cancel"), "TafsOutlineButton", () => scroller.Content = body));
        }
        scroller.Content = view;
    }

    /// <summary>Sends the report, one at a time: tafs cloud took it, or it didn't go through and nothing changed.</summary>
    async Task SendAsync(WordBook w)
    {
        if (sending || entry.Report is not { } offer || SendReport is not { } send) return;
        sending = true;
        ShowReport(w, ReportPhase.Sending);
        var took = await send(ReportPayment.Path(offer.TraceId));
        sending = false;
        if (took)
        {
            body.Children.Remove(report);
            Reported?.Invoke();
        }
        ShowReport(w, took ? ReportPhase.Done : ReportPhase.Failed);
    }

    static Button Wide(string text, string style, Action clicked)
    {
        var button = new Button { Content = text, Style = (Style)Application.Current.Resources[style], HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += (_, _) => clicked();
        return button;
    }

    /// <summary>KIFF's receipt, as the iPad's <c>loadReceipt</c>: read when the sheet opens; Try again when it couldn't be.</summary>
    async Task LoadAsync()
    {
        var w = App.Words;
        if (entry.Call?.Receipt is not { } r) return;
        if (Read is not { } read) return;
        // Read and checked here: "Checking KIFF's receipt on this Windows PC…" until then.
        var checking = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        checking.Children.Add(new ProgressRing { IsActive = true, Width = 16, Height = 16 });
        checking.Children.Add(Words(w.Get("receipt.checking_on_device", ("device", w.Get("device.windows"))), 13, "TafsSoft"));
        receipt.Child = checking;
        var (bytes, why) = await read(Receipt.Path(r.TraceId));
        if (bytes is null)
        {
            var again = new Button { Content = w.Get("common.try_again"), Style = (Style)Application.Current.Resources["TafsOutlineButton"] };
            again.Click += (_, _) => _ = LoadAsync();
            receipt.Child = Stack(Words(w.Get("receipt.not_loaded", ("why", why ?? "")), 13, "TafsNo"), again);
            return;
        }
        raw = bytes;
        check = ReceiptCheck.Check(w, bytes);
        var steps = Receipt.Steps(bytes);
        var mismatch = Receipt.Mismatch(w, entry, steps);
        // What happened is what KIFF signed for this line: only once checked here, and matching the line.
        if (steps.Count > 0 && Receipt.ShowsWhatHappened(check, mismatch is not null)) DrawSteps(w, steps);
        DrawReceipt(w, steps, mismatch);
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
    /// The receipt's panel, as the iPad's <c>receiptPanel</c>, by this PC's check:
    /// <list type="bullet">
    /// <item>checked, and the line agrees: "KIFF's receipt · checked on this Windows PC", "Signed by KIFF, unchanged since.",
    /// the decision and when it was issued, the full receipt with its signer, and Copy;</item>
    /// <item>checked, but the line disagrees with what KIFF signed: it doesn't check out, and which part;</item>
    /// <item>not signed yet: KIFF signs within a minute; after ten minutes, "If this stays…" with the receipt's id;</item>
    /// <item>anything else: it doesn't check out, "This receipt was changed after KIFF signed it, or it wasn't signed by KIFF".</item>
    /// </list>
    /// </summary>
    void DrawReceipt(WordBook w, IReadOnlyList<ReceiptStep> steps, string? mismatch)
    {
        switch (check)
        {
            case ReceiptCheck.Verified when mismatch is not null:
                Failed(w, steps, mismatch);
                break;
            case ReceiptCheck.Verified verified:
            {
                receipt.BorderBrush = Brush("TafsOk");
                var shown = new StackPanel { Spacing = 8 };
                shown.Children.Add(Label(((char)0xE73E).ToString(), w.Get("receipt.checked_on_device", ("device", w.Get("device.windows"))), "TafsOk"));
                shown.Children.Add(Words(w.Get("receipt.checked_why"), 13, "TafsSoft"));
                shown.Children.Add(Mono(Facts(w), 11.5));
                if (Full(w, steps, verified.Signer) is { } full) shown.Children.Add(full);
                shown.Children.Add(CopyButton(w));
                receipt.Child = shown;
                break;
            }
            case ReceiptCheck.NotSignedYet:
            {
                var shown = new StackPanel { Spacing = 8 };
                var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                head.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = Brush("TafsAmber"), VerticalAlignment = VerticalAlignment.Center });
                head.Children.Add(Words(w.Get("receipt.not_signed_yet"), 14, "TafsInk", FontWeights.SemiBold));
                shown.Children.Add(head);
                shown.Children.Add(Words(w.Get("receipt.signs_within"), 13, "TafsSoft"));
                if (DateTimeOffset.UtcNow - entry.At > TimeSpan.FromMinutes(10) && entry.Call?.Receipt is { } r)
                {
                    shown.Children.Add(Words(w.Get("receipt.if_it_stays"), 13, "TafsSoft"));
                    shown.Children.Add(Mono(r.TraceId, 11.5));
                }
                receipt.Child = shown;
                break;
            }
            default:
                Failed(w, steps, w.Get("receipt.changed"));
                break;
        }
    }

    /// <summary>It doesn't check out, as the iPad's <c>failedPanel</c>: why, the facts and the full receipt, with Copy, never Share.</summary>
    void Failed(WordBook w, IReadOnlyList<ReceiptStep> steps, string why)
    {
        receipt.BorderBrush = Brush("TafsNo");
        var shown = new StackPanel { Spacing = 8 };
        shown.Children.Add(Label(((char)0xEA39).ToString(), w.Get("record.kiff_receipt_does_not_check"), "TafsNo"));
        shown.Children.Add(Words(why, 13, "TafsSoft"));
        shown.Children.Add(Mono(Facts(w), 11.5));
        if (Full(w, steps, null) is { } full) shown.Children.Add(full);
        shown.Children.Add(CopyButton(w));
        receipt.Child = shown;
    }

    /// <summary>The receipt's decision and when it was issued; its id and signer are under the full receipt.</summary>
    string Facts(WordBook w)
    {
        var issued = Receipt.Field(raw!, "issued_at") is { } at && DateTimeOffset.TryParse(at, out var when)
            ? " · " + w.Get("receipt.issued", ("at", Receipt.Shown(w, when, TimeZoneInfo.Local))) : "";
        return Receipt.DecisionWords(w, entry) + issued;
    }

    /// <summary>
    /// Show the full receipt, as the iPad's <c>fullReceipt</c>: the receipt's id and its signer shortened, every step KIFF
    /// signed with its hash, and the signature. None without steps.
    /// </summary>
    Expander? Full(WordBook w, IReadOnlyList<ReceiptStep> steps, string? signer)
    {
        if (steps.Count == 0) return null;
        var trace = entry.Call?.Receipt?.TraceId ?? "";
        var lines = new StackPanel { Spacing = 6 };
        lines.Children.Add(Mono(signer is { Length: > 10 } s ? $"receipt {trace} · signer {s[..6]}…{s[^4..]}" : $"receipt {trace}", 11.5));
        for (var i = 0; i < steps.Count; i++)
        {
            var at = steps[i].At is { } t ? Receipt.Shown(w, t, TimeZoneInfo.Local, withDate: false) : "";
            lines.Children.Add(Mono($"{i + 1}  {steps[i].Words} · {at}\n   hash {steps[i].Hash}", 11.5));
        }
        if (Receipt.Field(raw!, "signature", attestation: true) is { Length: > 8 } sig)
            lines.Children.Add(Mono($"✎  trace {trace} · secp256k1 + keccak256\n   sig 0x{sig[..4]}…{sig[^4..]}", 11.5));
        return new Expander
        {
            Header = w.Get("receipt.show_full"), Content = lines, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
    }

    Button CopyButton(WordBook w)
    {
        var copy = new Button { Content = w.Get("receipt.copy"), Style = (Style)Application.Current.Resources["TafsOutlineButton"] };
        copy.Click += (_, _) =>
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(System.Text.Encoding.UTF8.GetString(raw!));
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        };
        return copy;
    }

    /// <summary>A seal and its words, in one colour, as the iPad's Label with checkmark.seal or xmark.seal.</summary>
    StackPanel Label(string glyph, string text, string colour)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        line.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, Foreground = Brush(colour) });
        line.Children.Add(Words(text, 14, colour, FontWeights.SemiBold));
        return line;
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
        Text = text, FontSize = size, FontFamily = Monospace.Face, Foreground = Brush("TafsDim"),
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
