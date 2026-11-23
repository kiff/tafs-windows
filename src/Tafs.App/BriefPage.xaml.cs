using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Tafs.Core.Home;
using Tafs.Core.Missions;

namespace Tafs.App;

/// <summary>
/// What the brief opens with, and what it can ask of the window. Every action goes through the routes the app already has:
/// <paramref name="Answer"/> opens the decision where it's answered (<see cref="Brief.Destination"/>, never with an answer
/// chosen), <paramref name="Later"/> is tafs cloud's unsigned post (null when taken, else its words), and nothing is signed
/// here.
/// </summary>
/// <param name="NeedsYouLine">"2 need you", for what Needs you holds that the brief doesn't show; none when it shows it all.</param>
/// <param name="Filter">The brief read again for a label (none is All); null keeps the one shown.</param>
/// <param name="TafOf">A decision's taf by tafs cloud's id: its name and colour, when it's listed.</param>
/// <param name="StartMove">The next move's Start: Coach opens with its words. None for someone who may not use Coach.</param>
public sealed record BriefOpening(Brief Brief, string? Label, string? NeedsYouLine,
    Func<string?, Task<Brief?>> Filter, Action<BriefDecision> Answer, Func<BriefDecision, Task<string?>> Later,
    Func<string, Task<MissionLog?>> Log, Action<BriefNextMove>? StartMove, Action OpenNeedsYou,
    Func<string, (string Name, int Colour)?> TafOf);

/// <summary>
/// Your brief, as the iPad's <c>BriefView</c> at a regular width: decisions beside Done, then the next move and the rest
/// under Done. Every word, count and order is tafs cloud's; a recommendation is shown, never acted on.
/// </summary>
public sealed partial class BriefPage : Page
{
    BriefOpening? opening;
    Brief? brief;
    string? label;
    bool expanded;
    /// <summary>Decisions put off here, until the next read drops them.</summary>
    readonly HashSet<string> setAside = [];
    /// <summary>The next move, dismissed here.</summary>
    bool moveDismissed;

    public BriefPage()
    {
        InitializeComponent();
        // Its colours are the window's theme's, known once it's in the window: drawn again then, and when the theme changes.
        Loaded += (_, _) => Show();
        ActualThemeChanged += (_, _) => Show();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        opening = e.Parameter as BriefOpening;
        if (opening is null) return;
        (brief, label) = (opening.Brief, opening.Label);
        Show();
    }

    void Show()
    {
        if (brief is not { } b || opening is not { } o) return;
        var w = App.Words;
        Heading.Text = b.Words.Title.Length > 0 ? b.Words.Title : b.Words.Tab;
        WhenText.Text = b.Words.When;
        When.Visibility = b.Words.When.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowChips(b);

        Decisions.Children.Clear();
        var open = b.Decisions.Where(d => !setAside.Contains(d.Id)).ToList();
        if (open.Count > 0)
        {
            Decisions.Children.Add(Eyebrow(b.Words.Decisions));
            foreach (var d in open) Decisions.Children.Add(DecisionCard(d, b.Words, o));
            if (b.More is { } more) Decisions.Children.Add(Line(more.Words, 13, "TafsSoftText"));
        }
        // Board 3, nothing to decide, as the iPad's BriefView.decisions(): a calm sun over a clear page, where decisions go.
        else
        {
            var art = Illustrations.BriefClear();
            art.HorizontalAlignment = HorizontalAlignment.Left;
            Decisions.Children.Add(art);
        }
        // Coach's chats waiting on their person, under the decisions as the iPad's coachWaiting: tafs cloud's words, opening Needs you.
        if (b.CoachWaiting is { } waiting)
        {
            var row = new Button { Style = Look("TafsBox"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
                    Children = { Line(waiting.Words, 14, "TafsInkText", semibold: true), Arrow() } } };
            AutomationProperties.SetAutomationId(row, "brief.coach_waiting");
            AutomationProperties.SetName(row, waiting.Words);
            row.Click += (_, _) => o.OpenNeedsYou();
            Decisions.Children.Add(row);
        }

        Side.Children.Clear();
        if (b.Done.Count > 0)
        {
            Side.Children.Add(Eyebrow(b.Words.Done));
            Side.Children.Add(DonePanel(b, o));
        }
        if (b.NextMove is { } move && !moveDismissed) Side.Children.Add(NextMove(move, b.Words, o));
        // The rest, with Needs you's own count whenever it holds more than the brief shows: nothing waiting is out of reach.
        if (b.Rest is not null || o.NeedsYouLine is not null)
        {
            var lines = new StackPanel { Spacing = 2 };
            if (b.Rest is { } rest) lines.Children.Add(Line(rest.Words, 14, "TafsInkText"));
            if (o.NeedsYouLine is { } count) lines.Children.Add(Line(count, 13, b.Rest is null ? "TafsInkText" : "TafsDimText", semibold: true));
            var row = new Button { Style = Look("TafsBox"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
                    Children = { lines, Arrow() } } };
            AutomationProperties.SetAutomationId(row, "brief.rest");
            AutomationProperties.SetName(row, string.Join(". ", lines.Children.OfType<TextBlock>().Select(t => t.Text)));
            row.Click += (_, _) => o.OpenNeedsYou();
            Side.Children.Add(row);
        }
        Credits.Text = b.Credits ?? "";
        Credits.Visibility = b.Credits is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The tafs list's chips, filtering the brief: All, two labels, then "+N".</summary>
    void ShowChips(Brief b)
    {
        Chips.Children.Clear();
        LabelRow.Visibility = b.Labels.Labels.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (b.Labels.Labels.Count == 0) return;
        var all = b.Words.All.Length > 0 ? b.Words.All : LabelChips.All(App.Words, b.Labels);
        Chips.Children.Add(Chip(all, null, false, label is null, () => _ = FilterAsync(null)));
        var (shown, hidden) = LabelChips.Row(b.Labels, expanded);
        foreach (var l in shown)
            Chips.Children.Add(Chip(l.Name, l.Count, l.Waiting > 0, label == l.Id, () => _ = FilterAsync(label == l.Id ? null : l.Id)));
        if (hidden > 0) Chips.Children.Add(Chip($"+{hidden}", null, false, false, () => { expanded = true; ShowChips(b); }));
    }

    async Task FilterAsync(string? id)
    {
        if (opening is null) return;
        label = id;
        setAside.Clear();
        // A read that fails keeps the brief shown.
        if (await opening.Filter(id) is { } read) brief = read;
        Show();
    }

    static Button Chip(string words, int? count, bool waiting, bool selected, Action choose)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        if (waiting) content.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = ChatViews.Themed("TafsAmber"), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = words, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center });
        if (count is { } n) content.Children.Add(new TextBlock { Text = n.ToString(CultureInfo.CurrentCulture), FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
        var chip = new Button { Content = content, MinHeight = 32, Style = Look(selected ? "TafsChipChosen" : "TafsChip") };
        AutomationProperties.SetName(chip, count is { } c ? $"{words}, {c}" : words);
        chip.Click += (_, _) => choose();
        return chip;
    }

    /// <summary>
    /// One decision, as the iPad's <c>DecisionCard</c>: its taf, Urgent or its deadline, the question, Coach's pick with why,
    /// Meanwhile, why it can't wait, the answers (the pick filled, a decline in red, Later beside them) and a note.
    /// </summary>
    Border DecisionCard(BriefDecision d, BriefWords words, BriefOpening o)
    {
        var lines = new StackPanel { Spacing = 10 };
        var head = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } } };
        if (o.TafOf(d.Ref.Taf) is { } taf)
        {
            var who = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            who.Children.Add(new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Background = RecordLineView.Colour(taf.Colour),
                Child = new TextBlock { Text = Tafs.Core.Taf.TafScreen.Initial(taf.Name), FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
            who.Children.Add(Line(taf.Name, 14, "TafsInkText", semibold: true));
            head.Children.Add(who);
        }
        FrameworkElement? side = null;
        if (d.Urgent)
        {
            var urgent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            urgent.Children.Add(new FontIcon { Glyph = "", FontSize = 10, Foreground = ChatViews.Themed("TafsOnFill") });
            if (words.Urgent.Length > 0) urgent.Children.Add(new TextBlock { Text = words.Urgent, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = ChatViews.Themed("TafsOnFill") });
            side = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 4, 7, 4), Background = ChatViews.Themed("TafsNo"), Child = urgent };
            AutomationProperties.SetAutomationId(side, "brief.urgent");
        }
        else if (d.Deadline is { } deadline)
            side = new TextBlock { Text = deadline, FontSize = 12, FontFamily = new FontFamily("Cascadia Mono, Consolas"), Foreground = ThemeBrush("TafsAmberText") };
        if (side is not null) { Grid.SetColumn(side, 1); side.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(side); }
        if (head.Children.Count > 0) lines.Children.Add(head);
        lines.Children.Add(Line(d.Question, 17, "TafsInkText", semibold: true));

        if (d.Recommended is { } pick && d.Why is { } why)
        {
            var rec = new StackPanel { Spacing = 4 };
            rec.Children.Add(new TextBlock { Text = $"{words.Recommends}: {pick.Words}", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap, Foreground = ChatViews.Themed("TafsCoach") });
            rec.Children.Add(Line(why, 14, "TafsInkText"));
            lines.Children.Add(new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Background = ThemeBrush("TafsCoachWash"), Child = rec });
        }
        if (d.Meanwhile.Length > 0)
        {
            var meanwhile = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, Style = Look("TafsInkText") };
            meanwhile.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = words.Meanwhile + " ", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
            meanwhile.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = d.Meanwhile });
            lines.Children.Add(meanwhile);
        }
        if (d.CantWait is { } cantWait)
            lines.Children.Add(new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Background = ThemeBrush("TafsNoEdge"),
                Child = new TextBlock { Text = cantWait, FontSize = 13, TextWrapping = TextWrapping.Wrap, Style = Look("TafsNoText") } });

        // The answers: Coach's pick filled; the others outlined, a decline in red; Later beside them, never when urgent.
        var others = d.Answers.Where(a => a != d.Recommended).ToList();
        if (d.Recommended is { } chosen)
        {
            var b = new Button { Content = chosen.Words, Style = Look("TafsPrimary") };
            AutomationProperties.SetAutomationId(b, "brief.answer." + chosen.Answer);
            b.Click += (_, _) => o.Answer(d);
            lines.Children.Add(b);
        }
        if (others.Count > 0 || d.OffersLater)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            foreach (var a in others)
            {
                var b = new Button { Content = a.Words, Style = Look(a.Decline ? "TafsOutlineNo" : "TafsOutlineButton") };
                AutomationProperties.SetAutomationId(b, "brief.answer." + a.Answer);
                b.Click += (_, _) => o.Answer(d);
                row.Children.Add(b);
            }
            if (d.OffersLater)
            {
                var later = new Button { Content = d.Later, Style = Look("TafsOutlineButton") };
                AutomationProperties.SetAutomationId(later, "brief.later");
                later.Click += async (_, _) =>
                {
                    later.IsEnabled = false;
                    // Set aside only once tafs cloud has moved it; refused, the card stays with tafs cloud's words.
                    var why = await o.Later(d);
                    if (why is null) setAside.Add(d.Id);
                    Error.Text = why ?? "";
                    Error.Visibility = why is null ? Visibility.Collapsed : Visibility.Visible;
                    Show();
                };
                row.Children.Add(later);
            }
            lines.Children.Add(row);
        }
        if (d.Note is { } note) lines.Children.Add(Line(note, 12, "TafsDimText"));

        var card = new Border { Background = ThemeBrush("TafsPanel"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(16),
            BorderBrush = d.Urgent ? ThemeBrush("TafsNoLine") : ThemeBrush("TafsLine"), Child = lines };
        AutomationProperties.SetAutomationId(card, "brief.decision." + d.Id);
        return card;
    }

    /// <summary>Done, case by case: each case's dot (green closed, amber waiting), name and words; its value and Evidence ›.</summary>
    Border DonePanel(Brief b, BriefOpening o)
    {
        var lines = new StackPanel { Spacing = 10 };
        for (var i = 0; i < b.Done.Count; i++)
        {
            var group = b.Done[i];
            if (i > 0) lines.Children.Add(new Rectangle { Height = 1, Style = Look("TafsLineRule") });
            foreach (var c in group.Cases)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                row.Children.Add(new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 6, 0, 0), VerticalAlignment = VerticalAlignment.Top,
                    Fill = (Brush)Application.Current.Resources[c.Closed ? "TafsOk" : c.Waiting ? "TafsAmber" : "TafsDim"] });
                var text = new StackPanel { Spacing = 2 };
                if (c.Name.Length > 0) text.Children.Add(Line(c.Name, 14, "TafsInkText", semibold: true));
                if (c.Words.Length > 0)
                {
                    var words = Line(c.Words, 13, "TafsSoftText");
                    if (c.Closed) words.Foreground = ChatViews.Themed("TafsOk");
                    text.Children.Add(words);
                }
                row.Children.Add(text);
                AutomationProperties.SetName(row, string.Join(", ", new[] { c.Name, c.Words }.Where(s => s.Length > 0)));
                lines.Children.Add(row);
            }
            if (group.Value is not null || group.Log is not null)
            {
                var foot = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } } };
                if (group.Value is { } value) foot.Children.Add(Line(value, 15, "TafsInkText", semibold: true));
                if (group.Log is { } mission)
                {
                    var evidence = new HyperlinkButton { Content = b.Words.Evidence + " ›", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = ChatViews.Themed("TafsCoach"), Padding = new Thickness(0) };
                    AutomationProperties.SetAutomationId(evidence, "brief.evidence." + mission);
                    evidence.Click += (_, _) => _ = ShowLogAsync(mission, group.Mission ?? "", o);
                    Grid.SetColumn(evidence, 1);
                    foot.Children.Add(evidence);
                }
                lines.Children.Add(foot);
            }
        }
        var panel = new Border { Background = ThemeBrush("TafsPanel"), BorderBrush = ThemeBrush("TafsLine"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Padding = new Thickness(16), Child = lines };
        AutomationProperties.SetAutomationId(panel, "brief.done");
        return panel;
    }

    /// <summary>The next move, as the iPad's: its words, Start (Coach opens with them) and Dismiss, on the amber edge.</summary>
    Border NextMove(BriefNextMove move, BriefWords words, BriefOpening o)
    {
        var lines = new StackPanel { Spacing = 10 };
        lines.Children.Add(new TextBlock { Text = words.NextMove.ToUpper(CultureInfo.CurrentCulture), FontSize = 12,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"), Foreground = ThemeBrush("TafsAmberText") });
        lines.Children.Add(Line(move.Words, 15, "TafsInkText", semibold: true));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (o.StartMove is { } start)
        {
            var b = new Button { Content = move.Start, Style = Look("TafsOutlineButton") };
            b.Click += (_, _) => { moveDismissed = true; start(move); Show(); };
            row.Children.Add(b);
        }
        var dismiss = new Button { Content = move.Dismiss, Style = Look("TafsOutlineButton") };
        dismiss.Click += (_, _) => { moveDismissed = true; Show(); };
        row.Children.Add(dismiss);
        lines.Children.Add(row);
        var panel = new Border { Background = ThemeBrush("TafsPanel"), BorderBrush = ChatViews.Themed("TafsAmberEdge"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Padding = new Thickness(16), Child = lines };
        AutomationProperties.SetAutomationId(panel, "brief.next-move");
        return panel;
    }

    /// <summary>
    /// A mission's log, as the iPad's <c>MissionLogView</c>, in a sheet of its own: read only, newest first, by day in this
    /// PC's time; each entry's author, its kind as a coloured word, the time, its words, evidence and value; a removed taf's
    /// entries greyed.
    /// </summary>
    async Task ShowLogAsync(string mission, string title, BriefOpening o)
    {
        var w = App.Words;
        var body = new StackPanel { Spacing = 12, MinWidth = 480 };
        body.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24 });
        var sheet = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = title.Length > 0 ? title : null, CloseButtonText = w.Get("common.back"),
            Content = new ScrollViewer { MaxHeight = 560, Content = body },
        };
        AutomationProperties.SetAutomationId(sheet, "mission.log");
        var showing = sheet.ShowAsync();
        // A log that can't be read keeps the spinner, as the iPad's MissionLogView: Back closes it.
        if (await o.Log(mission) is { } log)
        {
            body.Children.Clear();
            var today = DateTime.Now.Date;
            string? day = null;
            foreach (var item in log.Items)
            {
                var at = DateTimeOffset.TryParse(item.At, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t.ToLocalTime() : (DateTimeOffset?)null;
                var title2 = at is { } a ? Tafs.Core.Record.RecordScreen.DayTitle(w, a.Date, today) : "";
                if (title2 != day) { day = title2; if (title2.Length > 0) body.Children.Add(Eyebrow(title2)); }
                body.Children.Add(LogRow(item, at));
            }
        }
        await showing;
    }

    Border LogRow(LogItem item, DateTimeOffset? at)
    {
        var tint = (Brush)Application.Current.Resources[item.Kind switch
        {
            "delivered" => "TafsOk", "handed" => "TafsBlue", "stuck" => "TafsAmber", "decided" => "TafsInk", _ => "TafsDim",
        }];
        var lines = new StackPanel { Spacing = 6, Padding = new Thickness(12) };
        var head = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } } };
        var who = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        who.Children.Add(Line(item.ByWords.Length > 0 ? item.ByWords : item.By, 14, "TafsInkText", semibold: true));
        if (item.KindWords.Length > 0)
            who.Children.Add(new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 2, 6, 2), Background = ThemeBrush("TafsRaised"),
                Child = new TextBlock { Text = item.KindWords, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = tint } });
        head.Children.Add(who);
        if (at is { } a)
        {
            var time = new TextBlock { Text = a.ToString("HH:mm", CultureInfo.InvariantCulture), FontSize = 12, FontFamily = new FontFamily("Cascadia Mono, Consolas"), Style = Look("TafsDimText") };
            Grid.SetColumn(time, 1);
            head.Children.Add(time);
        }
        lines.Children.Add(head);
        lines.Children.Add(Line(item.Words, 14, "TafsInkText"));
        if (item.Evidence.Count > 0 || item.Value is not null)
        {
            var foot = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var e in item.Evidence)
                foot.Children.Add(new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 2, 6, 2), Background = ThemeBrush("TafsCoachWash"),
                    Child = new TextBlock { Text = e.Words, FontSize = 11, FontFamily = new FontFamily("Cascadia Mono, Consolas"), Foreground = ChatViews.Themed("TafsCoach") } });
            if (item.Value is { } value) foot.Children.Add(Line(value, 12, "TafsInkText", semibold: true));
            lines.Children.Add(foot);
        }
        var row = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(3) }, new ColumnDefinition() } };
        row.Children.Add(new Rectangle { Fill = item.Removed ? ThemeBrush("TafsLine") : tint });
        Grid.SetColumn(lines, 1);
        row.Children.Add(lines);
        var card = new Border { Background = ThemeBrush("TafsPanel"), BorderBrush = ThemeBrush("TafsLine"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Opacity = item.Removed ? 0.5 : 1, Child = row };
        AutomationProperties.SetName(card, string.Join(", ", new[] { item.ByWords, item.KindWords, item.Words, item.Value ?? "" }.Where(s => s.Length > 0)));
        return card;
    }

    static TextBlock Eyebrow(string text) => new() { Text = text.ToUpper(CultureInfo.CurrentCulture), Style = Look("TafsEyebrow") };

    static FontIcon Arrow()
    {
        var arrow = new FontIcon { Glyph = "", FontSize = 12, Foreground = ChatViews.Themed("TafsDim"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(arrow, 1);
        return arrow;
    }

    static TextBlock Line(string text, double size, string style, bool semibold = false) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Style = Look(style),
        FontWeight = semibold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
    };

    static Style Look(string key) => (Style)Application.Current.Resources[key];

    /// <summary>A theme brush in this page's theme, as NeedsYouPage finds one: Theme.xaml's dictionaries first.</summary>
    Brush ThemeBrush(string key)
    {
        var theme = ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        foreach (var merged in Application.Current.Resources.MergedDictionaries.Prepend(Application.Current.Resources))
            if (merged.ThemeDictionaries.TryGetValue(theme, out var found) && found is ResourceDictionary d && d.TryGetValue(key, out var brush) && brush is Brush b)
                return b;
        return (Brush)Application.Current.Resources[key];
    }
}
