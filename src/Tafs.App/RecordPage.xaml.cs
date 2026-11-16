using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Tafs.Core.NeedsYou;
using Tafs.Core.Record;

namespace Tafs.App;

/// <summary>One day of the Record, as drawn.</summary>
public sealed record RecordDayView(string Title, IReadOnlyList<RecordLineView> Entries);

/// <summary>
/// One line of the Record, as drawn: its status dot, its taf's colour, "Taf · what", the time and the detail; read out as
/// one, with what a tap does.
/// </summary>
public sealed record RecordLineView(string Title, string Time, string Detail, Brush TafColour, Visibility IsDone, Visibility IsWaiting, Visibility IsRefused,
    RecordEntry Entry)
{
    public string Name => $"{Title}, {Time}, {Detail}";
    public string Hint => App.Words.Get("record.shows_receipt");

    public static RecordLineView Of(RecordEntry e, TimeZoneInfo zone)
    {
        var (title, time) = RecordScreen.Line(e, zone);
        static Visibility Seen(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;
        var dot = RecordScreen.DotOf(e.Kind);
        return new(title, time, e.Detail, Colour(e.TafColor), Seen(dot == Dot.Went), Seen(dot == Dot.Waiting), Seen(dot == Dot.Stopped), e);
    }

    public static Brush Colour(int taf)
    {
        var rgb = TafColors.Of(taf);
        return new SolidColorBrush(ColorHelper.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    }
}

/// <summary>The Record: KIFF's activity for the session's tafs, or the iPad's sample; redrawn as each read lands.</summary>
public sealed partial class RecordPage : Page
{
    IReadOnlyList<RecordEntry> entries = [];
    RecordFilter filter = RecordFilter.All;
    string? taf;

    public RecordPage()
    {
        InitializeComponent();
        // The results are drawn in the page's own theme: again once it's in the window, and whenever the theme changes.
        Loaded += (_, _) => { if (searched is not null) ShowSearch(); };
        ActualThemeChanged += (_, _) => { if (searched is not null) ShowSearch(); };
    }

    /// <summary>How the search reads tafs cloud as the session; none signed out, or on the sample, and no box.</summary>
    public static Func<string, Task<Tafs.Core.Taf.CloudRead>>? Read { get; set; }

    /// <summary>A search already typed as the page opens: the stand-in's, for a screenshot (<c>--page record-search</c>).</summary>
    public static string? Typed { get; set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var w = App.Words;
        (Heading.Text, Lead.Text) = RecordScreen.Heading(w);
        SearchBox.PlaceholderText = w.Get("record.search_hint");
        SearchBox.Visibility = Read is null ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(SearchBox, SearchBox.PlaceholderText);
        ShowEntries(e.Parameter as IReadOnlyList<RecordEntry> ?? []);
        _ = ReadHintAsync();
        if (Typed is { } typed)
        {
            SearchBox.Text = typed;
            _ = SearchAsync(typed);
        }
    }

    /// <summary>The search, as the iPad's <c>recordSearch</c>: what was searched, its pages so far, and whether a page is on its way.</summary>
    string? searched;
    readonly List<RecordSearchPage> pages = [];
    bool searching;
    string? searchError;
    DispatcherQueueTimer? typing;

    /// <summary>
    /// The search forgotten, at a session change, as the iPad's <c>forgetSession</c> clears <c>recordSearch</c>: what was
    /// typed, found and on its way goes, and the box shows only when there's a session to search as.
    /// </summary>
    public void ClearSearch()
    {
        typing?.Stop();
        (searched, searching, searchError) = (null, false, null);
        pages.Clear();
        SearchBox.Text = "";
        SearchBox.PlaceholderText = App.Words.Get("record.search_hint");
        SearchBox.Visibility = Read is null ? Visibility.Collapsed : Visibility.Visible;
        ShowSearch();
    }

    /// <summary>tafs cloud's words for the box (<c>hint</c>), read once with an empty search, as the iPad's <c>loadRecordHint</c>.</summary>
    async Task ReadHintAsync()
    {
        if (Read is not { } read) return;
        var answer = (await read(RecordSearch.Path("", limit: 1))).Answer;
        // Read as another session than the one now: dropped.
        if (Read != read) return;
        if (answer is { } a && RecordSearch.ReadPage(a).Hint is { Length: > 0 } hint) SetHint(hint);
    }

    void SetHint(string hint)
    {
        SearchBox.PlaceholderText = hint;
        AutomationProperties.SetName(SearchBox, hint);
    }

    /// <summary>
    /// What's typed is searched 400 ms after the last key, so a word typed letter by letter is one search, as the iPad's
    /// <c>recordQueryTyped</c>; words already searched aren't searched again, and an emptied box shows the Record at once.
    /// </summary>
    void OnSearchTyped(object sender, TextChangedEventArgs e)
    {
        typing?.Stop();
        var asked = SearchBox.Text.Trim();
        if (asked.Length == 0)
        {
            searched = null;
            ShowSearch();
            return;
        }
        typing ??= DispatcherQueue.CreateTimer();
        typing.Interval = TimeSpan.FromMilliseconds(400);
        typing.IsRepeating = false;
        typing.Tick -= OnTypingPaused;
        typing.Tick += OnTypingPaused;
        typing.Start();
    }

    void OnTypingPaused(DispatcherQueueTimer timer, object args)
    {
        if (SearchBox.Text.Trim() is var asked && asked.Length > 0 && asked != searched) _ = SearchAsync(asked);
    }

    /// <summary>Enter searches what's typed at once, with no wait left to search it again.</summary>
    void OnSearchKey(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        typing?.Stop();
        e.Handled = true;
        _ = SearchAsync(SearchBox.Text);
    }

    /// <summary>
    /// The search (<c>GET /v1/record?q=</c>), as the iPad's <c>searchRecord</c>: <paramref name="more"/> reads the next page of
    /// the same search; a cursor tafs cloud refuses (409, a call made since) searches again from the top.
    /// </summary>
    async Task SearchAsync(string q, bool more = false)
    {
        if (Read is not { } read) return;
        var asked = q.Trim();
        if (asked.Length == 0)
        {
            searched = null;
            ShowSearch();
            return;
        }
        if (more && (searched != asked || searching || pages.LastOrDefault()?.NextCursor is null)) return;
        if (!more)
        {
            searched = asked;
            pages.Clear();
        }
        (searching, searchError) = (true, null);
        ShowSearch();
        var answer = await read(RecordSearch.Path(asked, more ? pages.Last().NextCursor : null));
        // Another search since, or another session: what was found for this one is never drawn.
        if (searched != asked || Read != read) return;
        searching = false;
        if (answer.Answer is { } a)
        {
            var page = RecordSearch.ReadPage(a);
            pages.Add(page);
            if (page.Hint.Length > 0) SetHint(page.Hint);
        }
        else if (more && answer.Status == 409)
        {
            await SearchAsync(asked);
            return;
        }
        else searchError = answer.Why ?? App.Words.Get("error.ours");
        ShowSearch();
    }

    /// <summary>
    /// While there's a search, its results in place of the Record, as the iPad's <c>RecordResults</c>: tafs cloud's groups, each
    /// call under its taf's name; nothing found in tafs cloud's words; More for the next page, else the end's words.
    /// </summary>
    void ShowSearch()
    {
        var on = searched is not null;
        Results.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        foreach (var e in new FrameworkElement[] { TafChipsRow, FilterChips, Days }) e.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (on) Empty.Visibility = Visibility.Collapsed;
        else Show();
        Results.Children.Clear();
        if (!on) return;
        var w = App.Words;
        foreach (var group in pages.SelectMany(p => p.Groups))
        {
            if (group.Title.Length > 0) Results.Children.Add(Text(group.Title, 12, "TafsDim", semibold: true));
            foreach (var hit in group.Hits) Results.Children.Add(HitRow(hit, opensCase: true));
        }
        if (pages.Count > 0 && pages.All(p => p.IsEmpty) && pages.Last().NoneWords is { } none) Results.Children.Add(Text(none, 14, "TafsSoft"));
        if (searchError is { } why) Results.Children.Add(Text(why, 13, "TafsNo"));
        if (searching) Results.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20 });
        else if (pages.LastOrDefault()?.NextCursor is not null)
        {
            var more = new Button { Content = w.Get("common.more"), Style = (Style)Application.Current.Resources["TafsOutlineButton"] };
            more.Click += (_, _) => _ = SearchAsync(searched!, more: true);
            Results.Children.Add(more);
        }
        else if (pages.LastOrDefault()?.EndWords is { } end) Results.Children.Add(Text(end, 13, "TafsDim"));
    }

    /// <summary>
    /// One call found, as the iPad's <c>RecordHitRow</c>: its taf's name; the call as its Activity draws it (its dot, what it
    /// was, the words under it, any money, its time); and what it's about, which opens the case (not inside a case).
    /// </summary>
    StackPanel HitRow(RecordHit hit, bool opensCase)
    {
        var row = new StackPanel { Spacing = 4 };
        row.Children.Add(new TextBlock
        {
            Text = hit.TafName.ToUpper(System.Globalization.CultureInfo.GetCultureInfo(App.Words.Language)), FontSize = 11,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"), Foreground = Look("TafsDim"),
        });
        var line = new Grid { ColumnSpacing = 10 };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition());
        var colour = RecordScreen.DotOf(hit.Line.Kind) switch { Dot.Went => "TafsOk", Dot.Waiting => "TafsAmber", _ => "TafsNo" };
        line.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = Look(colour), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) });
        var words = new StackPanel { Spacing = 2 };
        words.Children.Add(Text(hit.Line.Title, 15, "TafsInk", semibold: true));
        if (hit.Line.Detail.Length > 0) words.Children.Add(Text(hit.Line.Detail, 13, "TafsSoft"));
        if (hit.Line.Amount is { } amount) words.Children.Add(Text(amount, 14, "TafsInk"));
        if (hit.Line.Time.Length > 0) words.Children.Add(Text(hit.Line.Time, 12, "TafsDim"));
        Grid.SetColumn(words, 1);
        line.Children.Add(words);
        row.Children.Add(new Border
        {
            Child = line, Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(14), Background = Look("TafsPanel"),
            BorderBrush = Look("TafsLine"), BorderThickness = new Thickness(1),
        });
        if (opensCase && hit.About is { } about)
        {
            var open = new HyperlinkButton { Content = about + " ›", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Look("TafsAmberText"), Padding = new Thickness(0) };
            AutomationProperties.SetAutomationId(open, "record-case");
            open.Click += (_, _) => _ = OpenCaseAsync(about);
            row.Children.Add(open);
        }
        return row;
    }

    /// <summary>
    /// One case (<c>GET /v1/record/cases/{about}</c>), as the iPad's <c>RecordCaseSheet</c>: what it's about, the step that
    /// didn't go through in tafs cloud's words, then every call about it, oldest first. The Record never sends anything again.
    /// </summary>
    async Task OpenCaseAsync(string about)
    {
        if (Read is not { } read) return;
        var w = App.Words;
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20 });
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = about, CloseButtonText = w.Get("common.done"),
            Content = new ScrollViewer { Content = body, MaxHeight = 560 },
        };
        var shown = dialog.ShowAsync();
        var answer = await read(RecordSearch.CasePath(about));
        // The account changed while it was read: its steps are never drawn, and the case closes.
        if (Read != read)
        {
            dialog.Hide();
            return;
        }
        body.Children.Clear();
        if (answer.Answer is { } a && RecordSearch.ReadCase(a) is { } found)
        {
            if (found.FailedWords is { } failed) body.Children.Add(Text(failed, 15, "TafsNo", semibold: true));
            foreach (var step in found.Steps) body.Children.Add(HitRow(step, opensCase: false));
        }
        else body.Children.Add(Text(answer.Why ?? w.Get("error.ours"), 14, "TafsNo"));
        try { await shown; }
        catch (System.Runtime.InteropServices.COMException) { }
    }

    TextBlock Text(string text, double size, string colour, bool semibold = false) => new()
    {
        Text = text, FontSize = size, Foreground = Look(colour), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
        FontWeight = semibold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
    };

    Brush Look(string key) => ChatViews.ThemeBrush(ActualTheme, key);

    /// <summary>New lines, as a read lands: the chosen taf and filter stay, as the iPad's do across a refresh.</summary>
    public void ShowEntries(IReadOnlyList<RecordEntry> lines)
    {
        entries = lines;
        Show();
    }

    /// <summary>A line's sheet: its facts, what happened and KIFF's receipt.</summary>
    async void OnLine(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RecordEntry entry }) await ReceiptSheet.ShowAsync(XamlRoot, ActualTheme, entry);
    }

    /// <summary>Draws the chips, the chosen ones inverted, and the days the filter and the taf leave.</summary>
    void Show()
    {
        var w = App.Words;
        TafChips.Children.Clear();
        TafChips.Children.Add(Chip(RecordScreen.AllTafs(w), null, taf is null, () => taf = null));
        foreach (var (name, colour) in RecordScreen.TafChips(entries))
            TafChips.Children.Add(Chip(name, RecordLineView.Colour(colour), taf == name, () => taf = name));

        FilterChips.Children.Clear();
        foreach (var (f, title) in RecordScreen.Filters(w))
            FilterChips.Children.Add(Chip(title, null, filter == f, () => filter = f));

        var days = RecordScreen.Days(w, entries, filter, taf, DateTimeOffset.Now, TimeZoneInfo.Local);
        Days.ItemsSource = days.Select(d => new RecordDayView(d.Title, d.Entries.Select(x => RecordLineView.Of(x, TimeZoneInfo.Local)).ToList())).ToList();
        Empty.Text = RecordScreen.Empty(w, filter, taf);
        Empty.Visibility = days.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A chip, as the iPad's: 13, in a capsule; the chosen one inverts, the ink's fill with the ground's colour on it.</summary>
    Button Chip(string title, Brush? dot, bool chosen, Action choose)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (dot is not null) content.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = dot, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = title, FontSize = 13 });
        var chip = new Button
        {
            Content = content,
            Style = (Style)Application.Current.Resources[chosen ? "TafsChipChosen" : "TafsChip"],
        };
        AutomationProperties.SetName(chip, title);
        if (chosen) AutomationProperties.SetItemStatus(chip, "selected");
        chip.Click += (_, _) => { choose(); Show(); };
        return chip;
    }
}
