using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.Usage;

namespace Tafs.App;

/// <summary>What Plan &amp; usage shows: tafs cloud's balance and both periods' credits, as read.</summary>
public sealed record UsageReads(Balance? Balance, Credits? This, Credits? Previous);

/// <summary>
/// Plan &amp; usage. tafs cloud's reads come in their own change: until then the page shows its title only, never a balance
/// it hasn't read, or the iPad's sample. Buying, Raise the limit, who bills the plan and members' requests come with them.
/// </summary>
public sealed partial class UsagePage : Page
{
    UsageReads reads = new(null, null, null);
    bool previous;

    public UsagePage() => InitializeComponent();

    static Visibility Seen(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;
    static Style Look(string key) => (Style)Application.Current.Resources[key];

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var w = App.Words;
        NavTitle.Text = w.Get("usage.title");
        CreditsTitle.Text = w.Get("usage.credits").ToUpper(new System.Globalization.CultureInfo(w.Language));
        LastPeriod.Content = w.Get("usage.last_period");
        ThisPeriod.Content = w.Get("usage.this_period");
        reads = e.Parameter as UsageReads ?? reads;
        ShowBalance(reads.Balance);
        ShowCredits();
        // Signed in: read now, as the iPad's .task, never an earlier read shown as now.
        _ = LoadAsync();
    }

    void ShowBalance(Balance? b)
    {
        BalancePanel.Visibility = Seen(b is not null);
        BalanceUnread.Text = BalanceWhy.Text = balanceWhy ?? "";
        BalanceUnread.Visibility = Seen(b is null && balanceWhy is not null);
        BalanceWhy.Visibility = Seen(b is not null && balanceWhy is not null);
        if (b is null) return;
        BalancePanel.Style = Look(b.State switch { Tone.Waiting => "TafsCardWaiting", Tone.Warning => "TafsCardWarning", _ => "TafsCard" });
        BalanceWords.Text = b.Words;
        Meter.Visibility = Seen(b.MeterPct is not null);
        MeterOk.Visibility = Seen(b.State == Tone.Ok);
        MeterWarning.Visibility = Seen(b.State == Tone.Warning);
        MeterWaiting.Visibility = Seen(b.State == Tone.Waiting);
        BalanceDetail.Text = b.Detail ?? "";
        BalanceDetail.Visibility = Seen(b.Detail is not null);
        BalanceWarning.Text = b.Warning ?? "";
        BalanceWarning.Visibility = Seen(b.Warning is not null);
        BalanceWarning.Style = Look(b.State switch { Tone.Waiting => "TafsNoText", Tone.Warning => "TafsAmberWords", _ => "TafsInkText" });
    }

    void OnMeterSized(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width * (reads.Balance?.MeterPct ?? 0) / 100.0;
        MeterOk.Width = MeterWarning.Width = MeterWaiting.Width = width;
    }

    void OnLastPeriod(object sender, RoutedEventArgs e) { previous = true; ShowCredits(); }
    void OnThisPeriod(object sender, RoutedEventArgs e) { previous = false; ShowCredits(); }

    /// <summary>The Credits panel, unless tafs cloud says to leave it out (no plan and no run lately).</summary>
    void ShowCredits()
    {
        var c = previous ? reads.Previous : reads.This;
        // Shown with a period read, or to say why this period's read didn't come; not when tafs cloud says to leave it out.
        CreditsPanel.Visibility = Seen(((reads.This ?? reads.Previous) is not null || creditsWhy is not null) && reads.Balance?.CreditsPanel != false);
        LastPeriod.Style = Look(previous ? "TafsSegmentChosen" : "TafsSegment");
        ThisPeriod.Style = Look(previous ? "TafsSegment" : "TafsSegmentChosen");
        CreditsWords.Text = c?.Words ?? "";
        // This period's or the period before's read that failed: why, under the picker.
        var why = previous ? previousWhy : creditsWhy;
        CreditsWhy.Text = why ?? "";
        CreditsWhy.Visibility = Seen(why is not null);
        CreditsRows.Children.Clear();
        if (c is null || CreditsPanel.Visibility == Visibility.Collapsed) return;
        // Each taf that used credits, most first, in its own panel: it opens its credits and runs in that period, as the
        // iPad's TafCreditsView. A taf removed since has no id and doesn't open.
        foreach (var t in c.Tafs)
        {
            if (t.Taf is not { } taf || TafCreditsChosen is null)
            {
                CreditsRows.Children.Add(Card(Line(t.Words, 15)));
                continue;
            }
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Line(t.Words, 15));
            var chevron = new FontIcon { Glyph = "\uE76C", FontSize = 12, Style = Look("TafsAmberIcon"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(chevron, 1);
            row.Children.Add(chevron);
            var open = new Button { Content = Card(row), Style = Look("TafsQuietButton"), Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(open, t.Words);
            var inPrevious = previous;
            open.Click += (_, _) => TafCreditsChosen?.Invoke(taf, inPrevious);
            CreditsRows.Children.Add(open);
        }
        // Coach past its fair use: counted, its own row.
        if (c.Coach is { } coach) CreditsRows.Children.Add(Card(Line(coach, 15)));
        if (UsageScreen.ShowsNotCounted(c))
        {
            var not = new StackPanel { Spacing = 12 };
            if (c.NotCounted!.Words.Length > 0) not.Children.Add(Line(c.NotCounted.Words, 15, bold: true));
            foreach (var l in c.NotCounted.Lines) not.Children.Add(Line(l.Words, 14, soft: true));
            CreditsRows.Children.Add(Card(not));
        }
        // The note last, as the contract has it.
        if (c.Note is { } note)
        {
            var line = Line(note, 13, soft: true);
            line.Margin = new Thickness(4, 0, 4, 0);
            CreditsRows.Children.Add(line);
        }
    }

    static Border Card(UIElement child) => new() { Style = Look("TafsCard"), Child = child };

    /// <summary>A taf's row in Credits: its page on Card › Usage, in this period or the one before.</summary>
    public static event Action<string, bool>? TafCreditsChosen;

    static TextBlock Line(string text, double size, bool bold = false, bool soft = false) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Style = Look(soft ? "TafsSoftText" : "TafsInkText"),
        FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
    };
}
