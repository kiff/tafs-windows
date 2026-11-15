using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.Record;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// What a taf's page opens with: its head, its Card bar (none without a Card), and its Activity for a filter. tafs cloud's
/// comes in its own change; until then, the iPad's sample.
/// </summary>
public sealed record TafOpening(TafHead Head, CardBarLine? Card, Func<string, ActivityPage> Activity);

/// <summary>One day of a taf's Activity, as drawn: tafs cloud sends a day going on from the page before with no title.</summary>
public sealed record TafDayView(string Title, IReadOnlyList<TafLineView> Lines) { public Visibility HasTitle => Title.Length > 0 ? Visibility.Visible : Visibility.Collapsed; }

/// <summary>One line of a taf's Activity, as drawn: its dot, what it was, its time, the words under it, and any money it moved.</summary>
public sealed record TafLineView(string Title, string Time, string Detail, string Amount, Visibility HasAmount,
    Visibility IsDone, Visibility IsWaiting, Visibility IsRefused)
{
    public static TafLineView Of(ActivityLine l)
    {
        static Visibility Seen(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;
        var dot = RecordScreen.DotOf(l.Kind);
        return new(l.Title, l.Time, l.Detail, l.Amount ?? "", Seen(l.Amount is not null),
            Seen(dot == Dot.Went), Seen(dot == Dot.Waiting), Seen(dot == Dot.Stopped));
    }
}

/// <summary>A taf's page, on its Activity: the only tab built yet. Chat, the Card and Setup are shown, and come in their own changes.</summary>
public sealed partial class TafPage : Page
{
    TafOpening? opening;
    string filter = "all";

    public TafPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        opening = e.Parameter as TafOpening;
        if (opening is null) return;
        var w = App.Words;
        var head = opening.Head;
        var colour = RecordLineView.Colour(head.Colour);
        Avatar.Background = colour;
        Initial.Text = head.Initial;
        NameText.Text = head.Name;
        StateText.Text = head.State;

        AutomationProperties.SetName(Tabs, TafScreen.TabsName(w));
        var tabs = TafScreen.Tabs(w);
        for (var i = 0; i < tabs.Count; i++)
        {
            var (tab, title) = tabs[i];
            Tabs.ColumnDefinitions.Add(new ColumnDefinition());
            var chosen = tab == TafTab.Activity;
            var b = new Button { Content = title, Style = Look(chosen ? "TafsSegmentChosen" : "TafsSegment") };
            if (chosen) AutomationProperties.SetItemStatus(b, "selected");
            else
            {
                // Not built yet: shown, so the page has the iPad's tabs, and plainly off.
                b.IsEnabled = false;
                b.Opacity = 0.45;
            }
            Grid.SetColumn(b, i);
            Tabs.Children.Add(b);
        }

        if (opening.Card is { } card)
        {
            SmallCard.Background = colour;
            (CardHero.Text, CardUnder.Text) = (card.Hero, card.Under);
            AutomationProperties.SetName(CardBar, $"{card.Hero}, {card.Under}");
        }
        else CardBar.Visibility = Visibility.Collapsed;
        Show();
    }

    /// <summary>Draws the filters, the chosen one inverted, and the days it leaves.</summary>
    void Show()
    {
        if (opening is null) return;
        var w = App.Words;
        var page = opening.Activity(filter);
        FilterChips.Children.Clear();
        foreach (var f in page.Filters) FilterChips.Children.Add(Chip(f, f.Id == page.Filter));

        Days.ItemsSource = page.Groups.Select(g => new TafDayView(g.Title, g.Lines.Select(TafLineView.Of).ToList())).ToList();
        Empty.Text = TafActivity.Empty(w, page.Filter);
        Empty.Visibility = page.Groups.Count == 0 && page.EndWords is null ? Visibility.Visible : Visibility.Collapsed;
        EndWords.Text = page.EndWords ?? "";
        EndWords.Visibility = page.EndWords is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>A filter chip, as the Record's: 13, in a capsule; the chosen one inverts.</summary>
    Button Chip(ActivityFilter f, bool chosen)
    {
        var chip = new Button { Content = f.Title, FontSize = 13, Style = Look(chosen ? "TafsChipChosen" : "TafsChip") };
        AutomationProperties.SetName(chip, f.Title);
        if (chosen) AutomationProperties.SetItemStatus(chip, "selected");
        chip.Click += (_, _) => { filter = f.Id; Show(); };
        return chip;
    }

    static Style Look(string key) => (Style)Application.Current.Resources[key];
}
