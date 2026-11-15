using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.Home;
using Tafs.Core.Record;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// What a taf's page opens with: its head, its Card bar (none without a Card), its Activity for a filter, its Setup (none
/// yet from tafs cloud), and the tab it opens on. tafs cloud's comes in its own change; until then, the iPad's sample.
/// </summary>
public sealed record TafOpening(TafHead Head, CardBarLine? Card, Func<string, ActivityPage> Activity, SetupOpening? Setup = null,
    TafTab OpensOn = TafTab.Activity);

/// <summary>The sample's Setup, as taf-setup.png: its job and the notes it keeps.</summary>
public sealed record SetupOpening(string Job, IReadOnlyList<MemoryNote> Notes);

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

/// <summary>A taf's page, on its Activity or its Setup. Chat and the Card are shown, and come in their own changes.</summary>
public sealed partial class TafPage : Page
{
    TafOpening? opening;
    string filter = "all";
    TafTab tab;
    List<MemoryNote> notes = [];
    bool paused;

    public TafPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        opening = e.Parameter as TafOpening;
        if (opening is null) return;
        var head = opening.Head;
        var colour = RecordLineView.Colour(head.Colour);
        Avatar.Background = colour;
        Initial.Text = head.Initial;
        NameText.Text = head.Name;
        StateText.Text = head.State;
        PausedText.Text = App.Words.Get("taf.state.paused");
        notes = [.. opening.Setup?.Notes ?? []];
        tab = opening.OpensOn == TafTab.Setup && opening.Setup is not null ? TafTab.Setup : TafTab.Activity;

        if (opening.Card is { } card)
        {
            SmallCard.Background = colour;
            (CardHero.Text, CardUnder.Text) = (card.Hero, card.Under);
            AutomationProperties.SetName(CardBar, $"{card.Hero}, {card.Under}");
        }
        ShowTab();
        Show();
    }

    /// <summary>
    /// The tabs, the chosen one filled, and its pane. Activity and Setup are built; Chat and the Card are shown, so the
    /// page has the iPad's tabs, and plainly off. The Card bar is above Activity only here, as on the iPad it's above Chat
    /// and Activity.
    /// </summary>
    void ShowTab()
    {
        var w = App.Words;
        Tabs.Children.Clear();
        Tabs.ColumnDefinitions.Clear();
        AutomationProperties.SetName(Tabs, TafScreen.TabsName(w));
        var tabs = TafScreen.Tabs(w);
        for (var i = 0; i < tabs.Count; i++)
        {
            var (each, title) = tabs[i];
            Tabs.ColumnDefinitions.Add(new ColumnDefinition());
            var b = new Button { Content = title, Style = Look(each == tab ? "TafsSegmentChosen" : "TafsSegment") };
            if (each == tab) AutomationProperties.SetItemStatus(b, "selected");
            if (each is TafTab.Activity || (each is TafTab.Setup && opening?.Setup is not null))
                b.Click += (_, _) => { tab = each; ShowTab(); };
            else
            {
                b.IsEnabled = false;
                b.Opacity = 0.45;
            }
            Grid.SetColumn(b, i);
            Tabs.Children.Add(b);
        }
        ActivityPane.Visibility = tab == TafTab.Activity ? Visibility.Visible : Visibility.Collapsed;
        SetupPane.Visibility = tab == TafTab.Setup ? Visibility.Visible : Visibility.Collapsed;
        CardBar.Visibility = tab == TafTab.Activity && opening?.Card is not null ? Visibility.Visible : Visibility.Collapsed;
        if (tab == TafTab.Setup) ShowSetup();
    }

    /// <summary>
    /// Setup, as taf-setup.png: the job; Starts when and Results go to, shown and off until their screens are built; what
    /// it remembers, each note with Forget; and Pause.
    /// </summary>
    void ShowSetup()
    {
        if (opening?.Setup is not { } setup) return;
        var w = App.Words;
        SetupPane.Children.Clear();
        SetupPane.Children.Add(Panel(Eyebrow("setup.job"), Words(setup.Job, 15, "TafsInkText")));
        foreach (var (key, glyph) in new[] { ("setup.starts_when", ""), ("setup.results_go_to", "") })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 15, Style = Look("TafsAmberIcon") });
            row.Children.Add(new TextBlock { Text = w.Get(key), FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Style = Look("TafsAmberWords") });
            var link = Panel(row);
            link.Opacity = 0.45;
            SetupPane.Children.Add(link);
        }

        var memory = new StackPanel { Spacing = 12 };
        memory.Children.Add(Eyebrow("taf.memory.title"));
        if (notes.Count == 0) memory.Children.Add(Words(w.Get("taf.memory.empty"), 14, "TafsSoftText"));
        foreach (var note in notes)
        {
            var line = new Grid { ColumnSpacing = 10 };
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var said = new StackPanel { Spacing = 3 };
            said.Children.Add(Words(note.Content, 15, "TafsInkText"));
            said.Children.Add(Words(TafSetup.NoteLine(note, TimeZoneInfo.Local), 12, "TafsSoftText"));
            var forget = new Button { Content = w.Get("taf.memory.forget"), Style = Look("TafsQuietButton"), VerticalAlignment = VerticalAlignment.Top };
            AutomationProperties.SetName(forget, w.Get("taf.memory.forget_label", ("note", note.Content)));
            forget.Click += async (_, _) => await ForgetAsync(note);
            Grid.SetColumn(forget, 1);
            line.Children.Add(said);
            line.Children.Add(forget);
            memory.Children.Add(line);
        }
        memory.Children.Add(Words(w.Get("taf.memory.explains"), 12, "TafsSoftText"));
        SetupPane.Children.Add(Panel(memory));

        var pause = new Button { Content = w.Get(paused ? "taf.resume" : "taf.pause"), Style = Look("TafsOutlineButton"), HorizontalAlignment = HorizontalAlignment.Stretch };
        pause.Click += (_, _) =>
        {
            paused = !paused;
            WorkingChip.Visibility = paused ? Visibility.Collapsed : Visibility.Visible;
            PausedChip.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
            ShowSetup();
        };
        SetupPane.Children.Add(pause);
    }

    /// <summary>Forget asks first, as the iPad's: it can't be undone. The sample's note goes from this page.</summary>
    async Task ForgetAsync(MemoryNote note)
    {
        var w = App.Words;
        var ask = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = w.Get("taf.memory.forget_title"),
            Content = w.Get("taf.memory.forget_body", ("note", note.Content)),
            PrimaryButtonText = w.Get("taf.memory.forget"),
            CloseButtonText = w.Get("common.cancel"),
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ActualTheme,
        };
        if (await ask.ShowAsync() != ContentDialogResult.Primary) return;
        notes.Remove(note);
        ShowSetup();
    }

    TextBlock Eyebrow(string key) => new() { Text = Sidebar.Eyebrow(App.Words, key), Style = Look("TafsEyebrow") };

    static TextBlock Words(string text, double size, string style) =>
        new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Style = Look(style) };

    static Border Panel(params UIElement[] children)
    {
        var inside = new StackPanel { Spacing = 8 };
        foreach (var c in children) inside.Children.Add(c);
        return new Border { Style = Look("TafsCard"), Child = inside };
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
