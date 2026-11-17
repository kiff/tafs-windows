using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>Which of the Card tab's screens shows: the Card itself, its versions, or its usage.</summary>
public enum CardScreen { Main, Versions, Usage }

/// <summary>
/// The Card tab's two rows for whoever manages a tafs cloud taf, as the iPad's <c>card(_:)</c>: "Changes to how it runs"
/// (tafs cloud's title and summary from its Setup), which opens its versions (the iPad's <c>VersionsPanel</c>, newest first,
/// the live one marked, each with who made it, when, a new Card, and its ended trials); and Usage, which opens its credits
/// this period (<c>TafCreditsView</c>), its runs newest first with More. Installing or trying a version signs a Card switch
/// and comes with its own change.
/// </summary>
public sealed partial class TafPage
{
    CardScreen cardScreen;
    TafVersions? versions;
    string? versionsWhy;
    TafCredits? credits;
    string? creditsWhy;
    bool creditsMore;

    /// <summary>The two rows, after the halt row: "Changes to how it runs" with its summary, and Usage.</summary>
    void AddCardRows()
    {
        if (opening is not { CloudId: not null, Manages: true }) return;
        var w = App.Words;
        var page = setupRead.Page;
        var title = page?.Versions.Title is { Length: > 0 } t ? t : w.Get("version.changes_row");
        CardPane.Children.Add(CardRow(title, page?.Versions.Summary is { Length: > 0 } s ? s : null, "", "card.versions", CardScreen.Versions));
        CardPane.Children.Add(CardRow(w.Get("usage.label"), null, "", "card.usage", CardScreen.Usage));
    }

    Button CardRow(string title, string? line, string glyph, string id, CardScreen screen)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 15, Style = Look("TafsAmberIcon"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
        var words = new StackPanel { Spacing = 4 };
        words.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Style = Look("TafsAmberWords") });
        if (line is not null) words.Children.Add(Words(line, 13, "TafsSoftText"));
        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        var chevron = new FontIcon { Glyph = "", FontSize = 12, Style = Look("TafsAmberIcon"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(chevron, 2);
        row.Children.Add(chevron);
        var b = new Button { Content = Panel(row), Style = Look("TafsQuietButton"), Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(b, line is null ? title : title + ", " + line);
        AutomationProperties.SetAutomationId(b, id);
        b.Click += (_, _) => OpenCardScreen(screen);
        return b;
    }

    void OpenCardScreen(CardScreen screen)
    {
        cardScreen = screen;
        ShowCards();
        if (screen == CardScreen.Versions) _ = LoadVersionsAsync();
        if (screen == CardScreen.Usage) _ = LoadCreditsAsync(more: false);
    }

    /// <summary>The Card tab on one of its screens: Back to the Card, its title, then what it shows. False on the Card itself.</summary>
    bool ShowCardScreen()
    {
        if (cardScreen == CardScreen.Main || opening?.CloudId is null) return false;
        var w = App.Words;
        CardPane.Children.Clear();
        var back = new HyperlinkButton { Padding = new Thickness(0) };
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        label.Children.Add(new FontIcon { Glyph = "", FontSize = 12 });
        label.Children.Add(new TextBlock { Text = w.Get("taf.section.card") });
        back.Content = label;
        AutomationProperties.SetName(back, w.Get("common.back"));
        back.Click += (_, _) => { cardScreen = CardScreen.Main; ShowCards(); };
        CardPane.Children.Add(back);
        if (cardScreen == CardScreen.Versions) ShowVersions(w);
        else ShowCredits(w);
        return true;
    }

    async Task LoadVersionsAsync()
    {
        if (opening is not { CloudId: { } id, Read: { } read }) return;
        var answer = await read(TafVersions.Path(id));
        if (opening?.CloudId != id) return;
        // Its open trial with them, so Try it and Resume say what's open.
        _ = LoadTrialAsync();
        if (answer.Answer is { } a && TafVersions.Read(a) is { } v) (versions, versionsWhy) = (v, null);
        else versionsWhy = answer.Why ?? App.Words.Get("kiff.kiff_did_not_answer_expected");
        if (tab == TafTab.Card && cardScreen == CardScreen.Versions) ShowCards();
    }

    /// <summary>Versions, as the iPad's panel: the intro, a switch under way, then each version newest first, or one so far.</summary>
    void ShowVersions(Tafs.Core.Words.WordBook w)
    {
        var title = Words(w.Get("version.title"), 22, "TafsInkText");
        title.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        CardPane.Children.Add(title);
        var inside = new StackPanel { Spacing = 12 };
        inside.Children.Add(Words(w.Get("taf.try_version_first_you_chat"), 13, "TafsSoftText"));
        if (versions?.Switching is { } n) inside.Children.Add(Words(w.Get("version.installing_number", ("version", n.ToString(CultureInfo.InvariantCulture))), 14, "TafsAmberWords"));
        if (versions is { } list)
        {
            if (list.Versions.Count <= 1) inside.Children.Add(Words(w.Get("version.first_only"), 14, "TafsSoftText"));
            foreach (var v in list.Newest) inside.Children.Add(VersionRow(w, v));
        }
        else if (versionsWhy is null) inside.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left });
        if (versionsWhy is { } why) inside.Children.Add(Words(why, 13, "TafsNoText"));
        AddInstallState(w, inside);
        if (trialWhy is { } trialNotRead) inside.Children.Add(Words(trialNotRead, 13, "TafsNoText"));
        CardPane.Children.Add(Panel(inside));
    }

    /// <summary>A version: "Version 2" and Live now, what changed, who made it and when, and each trial it had.</summary>
    StackPanel VersionRow(Tafs.Core.Words.WordBook w, TafVersion v)
    {
        var row = new StackPanel { Spacing = 6, Padding = new Thickness(0, 4, 0, 4) };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        head.Children.Add(new TextBlock { Text = w.Get("version.number", ("version", v.N.ToString(CultureInfo.InvariantCulture))), FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Style = Look("TafsInkText") });
        if (v.Live)
            head.Children.Add(new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 2, 7, 2), Background = ChatViews.ThemeBrush(ActualTheme, "TafsOk"),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = w.Get("version.live_now"), FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White) } });
        else if (openTrial?.N == v.N) head.Children.Add(Capsule(w.Get("version.trying"), (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TafsAmber"]));
        row.Children.Add(head);
        if (v.Note is { Length: > 0 } note) row.Children.Add(Words(note, 14, "TafsInkText"));
        row.Children.Add(Words(TafVersions.Byline(w, v, TimeZoneInfo.Local), 12, "TafsDimText"));
        foreach (var t in v.Trials) row.Children.Add(Words(TafVersions.Tried(w, t, TimeZoneInfo.Local), 12, "TafsSoftText"));
        // Try it, or Resume the trial: the version beside the live one, under a Card of its own; and Install this version.
        var actions = TrialButtons(w, v) ?? new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (InstallButton(w, v) is { } install) actions.Children.Add(install);
        if (actions.Children.Count > 0) row.Children.Add(actions);
        return row;
    }

    async Task LoadCreditsAsync(bool more)
    {
        if (opening is not { CloudId: { } id, Read: { } read }) return;
        if (more && credits?.Next is null) return;
        creditsMore = more;
        if (more) ShowCards();
        var answer = await read(TafCredits.Path(id, more ? credits!.Next : null, opening.CreditsPrevious));
        creditsMore = false;
        if (opening?.CloudId != id) return;
        if (answer.Answer is { } a && TafCredits.Read(a) is { } page) (credits, creditsWhy) = (more && credits is { } before ? before.With(page) : page, null);
        else creditsWhy = answer.Why ?? App.Words.Get("kiff.kiff_did_not_answer_expected");
        if (tab == TafTab.Card && cardScreen == CardScreen.Usage) ShowCards();
    }

    /// <summary>Its credits this period, in tafs cloud's words, then its runs newest first, More while there are more.</summary>
    void ShowCredits(Tafs.Core.Words.WordBook w)
    {
        var title = Words(w.Get("usage.label"), 22, "TafsInkText");
        title.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        CardPane.Children.Add(title);
        var inside = new StackPanel { Spacing = 10 };
        if (credits is { } c)
        {
            if (c.Words.Length > 0) inside.Children.Add(Words(c.Words, 15, "TafsInkText"));
            foreach (var run in c.Runs)
            {
                var line = new Grid { ColumnSpacing = 10 };
                line.ColumnDefinitions.Add(new ColumnDefinition());
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.Children.Add(Words(run.Words, 14, "TafsSoftText"));
                var at = new TextBlock { Text = run.At.ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.GetCultureInfo(w.Language)), FontSize = 12,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"), Style = Look("TafsDimText") };
                Grid.SetColumn(at, 1);
                line.Children.Add(at);
                if (run.Opens)
                {
                    // A run that opens, as the iPad's CreditsRunRow: its own page, what it did step by step, on Activity.
                    var open = new Button { Content = line, Style = Look("TafsQuietButton"), Padding = new Thickness(0),
                        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                    AutomationProperties.SetName(open, run.Words);
                    var id = run.Run;
                    open.Click += (_, _) =>
                    {
                        tab = TafTab.Activity;
                        ShowTab();
                        OpenRun(id);
                    };
                    inside.Children.Add(open);
                }
                else inside.Children.Add(line);
            }
            if (c.Next is not null)
            {
                var more = new Button { Content = w.Get(creditsMore ? "common.loading" : "common.more"),
                    Style = Look("TafsOutlineButton"), IsEnabled = !creditsMore, HorizontalAlignment = HorizontalAlignment.Left };
                more.Click += (_, _) => _ = LoadCreditsAsync(more: true);
                inside.Children.Add(more);
            }
        }
        else if (creditsWhy is null) inside.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left });
        if (creditsWhy is { } why) inside.Children.Add(Words(why, 13, "TafsNoText"));
        CardPane.Children.Add(Panel(inside));
    }
}
