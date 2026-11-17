using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Home;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>Which of Setup's screens shows: Setup itself, Starts when or Results go to.</summary>
public enum SetupScreen { Main, StartsWhen, ResultsGoTo, Door }

/// <summary>
/// Setup › Starts when and Results go to (P40), as the iPad's <c>StartsWhenView</c> and <c>ResultsGoToView</c>, from the taf's
/// dial (<see cref="TafKnob"/>). What changes at once is here: a follow-up removed, every start paused or resumed, tafs' own
/// places switched. A change that makes the taf's next version (a schedule's − and +, If busy, an app's event or place) waits
/// on Make it live, which comes with the versions' own change: until then the dial's waiting version is said, and those show
/// where they stand.
/// </summary>
public sealed partial class TafPage
{
    SetupScreen setupScreen;
    TafKnob? knob;
    string? knobWhy;
    /// <summary>A change on its way: one at a time, as the iPad's <c>busy</c>.</summary>
    bool knobBusy;
    /// <summary>Why the last change didn't go through, under the screen's title.</summary>
    string? knobSaid;
    /// <summary>tafs' own places on the live taf, as Home sent them and each <c>PUT …/results</c> answers; none until known.</summary>
    IReadOnlyList<string>? liveResults;

    /// <summary>Starts when and Results go to, under the job, each opening its own screen.</summary>
    void AddSetupLinks()
    {
        foreach (var (screen, key, glyph) in new[] { (SetupScreen.StartsWhen, "setup.starts_when", ""), (SetupScreen.ResultsGoTo, "setup.results_go_to", "") })
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 15, Style = Look("TafsAmberIcon") });
            var words = new TextBlock { Text = App.Words.Get(key), FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Style = Look("TafsAmberWords") };
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            var chevron = new FontIcon { Glyph = "", FontSize = 12, Style = Look("TafsAmberIcon") };
            Grid.SetColumn(chevron, 2);
            row.Children.Add(chevron);
            var link = new Button
            {
                Content = Panel(row), Style = Look("TafsQuietButton"), Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetName(link, App.Words.Get(key));
            AutomationProperties.SetAutomationId(link, screen == SetupScreen.StartsWhen ? "setup.starts-when" : "setup.results-go-to");
            link.Click += (_, _) => OpenSetupScreen(screen);
            SetupPane.Children.Add(link);
        }
    }

    void OpenSetupScreen(SetupScreen screen)
    {
        (setupScreen, knobSaid) = (screen, null);
        ShowSetup();
        if (screen != SetupScreen.Main) _ = LoadKnobAsync();
        // Its doors, on Starts when, for whoever manages the taf.
        if (screen == SetupScreen.StartsWhen) _ = LoadDoorsAsync();
    }

    /// <summary>The dial, read again on each screen and after each change, as the iPad's <c>loadKnob</c>; a failed read keeps the last.</summary>
    async Task LoadKnobAsync()
    {
        if (opening?.CloudId is not { } id || opening.Read is not { } read) return;
        var answer = await read(TafKnob.Path(id));
        if (opening?.CloudId != id) return;
        if (answer.Answer is { } a && TafKnob.Read(a) is { } k) (knob, knobWhy) = (k, null);
        else knobWhy = answer.Why ?? App.Words.Get("kiff.kiff_did_not_answer_expected");
        if (tab == TafTab.Setup && setupScreen != SetupScreen.Main) ShowSetup();
    }

    /// <summary>A change sent as the session, one at a time; the dial read again after it went through, else why it didn't.</summary>
    async Task<CloudRead?> ChangeAsync(HttpMethod method, string path, byte[]? body)
    {
        if (knobBusy || opening?.Change is not { } change) return null;
        (knobBusy, knobSaid) = (true, null);
        // Drawn again once the click is over: rebuilding the screen under a switch while it's still being pressed passed the
        // press to the switch that took its place, so a second place turned too.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ShowSetup);
        var sent = await change(method, path, body);
        knobBusy = false;
        if (sent.Answer is null) knobSaid = sent.Why ?? App.Words.Get("error.ours");
        ShowSetup();
        if (sent.Answer is not null) await LoadKnobAsync();
        return sent;
    }

    /// <summary>The screen's head: Setup to go back, then its title, then why the last change didn't go through.</summary>
    void SetupScreenHead(string titleKey)
    {
        var w = App.Words;
        var back = new HyperlinkButton { Padding = new Thickness(0) };
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        label.Children.Add(new FontIcon { Glyph = "", FontSize = 12 });
        label.Children.Add(new TextBlock { Text = w.Get("taf.section.setup") });
        back.Content = label;
        AutomationProperties.SetName(back, w.Get("common.back"));
        back.Click += (_, _) => OpenSetupScreen(SetupScreen.Main);
        SetupPane.Children.Add(back);
        var title = Words(w.Get(titleKey), 22, "TafsInkText");
        title.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        SetupPane.Children.Add(title);
        if (knobSaid is { } said) SetupPane.Children.Add(Words(said, 13, "TafsNoText"));
    }

    /// <summary>The dial not read yet: reading, or why not with Try again.</summary>
    void KnobNotRead(string readingKey)
    {
        if (knobWhy is { } why)
        {
            SetupPane.Children.Add(Words(why, 13, "TafsNoText"));
            var again = new Button { Content = App.Words.Get("common.try_again") };
            again.Click += (_, _) => _ = LoadKnobAsync();
            SetupPane.Children.Add(again);
        }
        else SetupPane.Children.Add(Words(App.Words.Get(readingKey), 15, "TafsDimText"));
    }

    /// <summary>
    /// A change to the dial waiting as its next version, as the iPad's <c>KnobPendingPanel</c>: "DRAFT · V4 · NOT LIVE" and its
    /// words, for whoever manages the taf. Make it live comes with the versions' own change.
    /// </summary>
    void KnobPending(TafKnob k)
    {
        if (k.Pending is not { } pending || opening?.Manages != true) return;
        var panel = Panel();
        panel.BorderBrush = Faint("TafsAmber", 0.45);
        var inside = (StackPanel)panel.Child;
        inside.Children.Add(new TextBlock
        {
            Text = App.Words.Get("knob.not_live", ("version", pending.Version.ToString(CultureInfo.InvariantCulture))), FontSize = 10,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
            Style = Look("TafsAmberWords"),
        });
        foreach (var line in k.DraftWords) inside.Children.Add(Words(line, 13, "TafsSoftText"));
        SetupPane.Children.Add(panel);
    }

    /// <summary>
    /// Starts when, as the iPad's <c>StartsWhenView</c>: its schedule and when it next runs, what it does if still working;
    /// the app events it listens to; the follow-ups it set itself, each with Remove; Change when it starts with Coach; then
    /// Pause every start, or Resume. Every line is tafs cloud's.
    /// </summary>
    void ShowStartsWhen()
    {
        var w = App.Words;
        SetupScreenHead("setup.starts_when");
        if (knob is not { } k)
        {
            KnobNotRead("setup.reading_starts");
            return;
        }
        var manages = opening?.Manages == true;
        KnobPending(k);

        var schedule = new List<UIElement> { Eyebrow("setup.schedule") };
        var schedules = k.Schedules.ToList();
        if (schedules.Count == 0) schedule.Add(Words(w.Get(opening?.Starts?.Unscheduled == true ? "setup.no_start" : "setup.no_schedule"), 15, "TafsSoftText"));
        foreach (var item in schedules)
        {
            var line = Words(item.Line, 15, "TafsInkText");
            line.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            schedule.Add(line);
            if (item.Size is { } size) schedule.Add(Words(size.Shown, 13, "TafsSoftText"));
        }
        if (opening?.Starts is { NextRun: { } next } starts && !paused)
            schedule.Add(Words((starts.NextRunWords ?? w.Get("taf.now.next_run", ("time", next.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture))))
                + " · " + (opening.TimeZone ?? "UTC"), 13, "TafsDimText"));
        if (schedules.Count > 0 && k.Busy is { } busy)
            schedule.Add(Words(w.Get("setup.if_busy") + " · " + w.Get(busy == "latest" ? "setup.run_after" : "setup.skip_it"), 13, "TafsSoftText"));
        SetupPane.Children.Add(Panel([.. schedule]));

        var events = k.AppEvents.ToList();
        if (events.Count > 0)
        {
            var panel = new List<UIElement> { Eyebrow("setup.app_events") };
            foreach (var item in events)
            {
                var label = Words(item.Label, 15, "TafsInkText");
                label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                panel.Add(label);
                if (item.Words.Length > 0) panel.Add(Words(item.Words, 13, "TafsSoftText"));
                if (item.Approval?.State == "approved" || (item.On && item.Approval?.State != "needed"))
                {
                    var listening = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    listening.Children.Add(new FontIcon { Glyph = "", FontSize = 13, Style = Look("TafsOkIcon") });
                    listening.Children.Add(new TextBlock { Text = w.Get("setup.listening"), FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Style = Look("TafsOkText") });
                    panel.Add(listening);
                }
            }
            panel.Add(Words(w.Get("setup.approve_note"), 12, "TafsDimText"));
            SetupPane.Children.Add(Panel([.. panel]));
        }

        // Its doors: where work reaches it from outside the app.
        AddDoorsPanel();

        var followUps = k.FollowUps.ToList();
        if (followUps.Count > 0)
        {
            var panel = new List<UIElement> { Eyebrow("setup.follow_ups") };
            foreach (var item in followUps)
            {
                var row = new Grid { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var said = new StackPanel { Spacing = 2 };
                said.Children.Add(Words(item.Line, 15, "TafsInkText"));
                if (item.Expires is { } at)
                    said.Children.Add(Words(at.ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.GetCultureInfo(w.Language)), 12, "TafsDimText"));
                row.Children.Add(said);
                if (manages && item.FollowUp is { } fid && opening?.CloudId is { } id)
                {
                    var remove = new Button { Content = w.Get("setup.remove"), Style = Look("TafsOutlineNo"), IsEnabled = !knobBusy, VerticalAlignment = VerticalAlignment.Top };
                    AutomationProperties.SetName(remove, w.Get("setup.remove") + ", " + item.Line);
                    remove.Click += async (_, _) => await ChangeAsync(HttpMethod.Delete, TafKnob.FollowUpPath(id, fid), null);
                    Grid.SetColumn(remove, 1);
                    row.Children.Add(remove);
                }
                panel.Add(row);
            }
            SetupPane.Children.Add(Panel([.. panel]));
        }

        if (manages && opening?.Improve is { } ask)
        {
            var coach = new Button { Content = w.Get("setup.change_starts"), Style = Look("TafsOutlineButton"), HorizontalAlignment = HorizontalAlignment.Stretch };
            coach.Click += (_, _) => ask();
            SetupPane.Children.Add(coach);
        }

        // Every start, as the iPad's: its own schedules, the dial's others and its follow-ups. Not for a taf that works from its chat.
        if (manages && k.CanPause && opening?.CloudId is { } taf)
        {
            var pause = new Button
            {
                Content = w.Get(paused ? "setup.resume_every" : "setup.pause_every"), IsEnabled = !knobBusy, HorizontalAlignment = HorizontalAlignment.Stretch,
                Style = Look(paused ? "TafsOutlineOk" : "TafsOutlineNo"),
            };
            AutomationProperties.SetAutomationId(pause, "setup.pause-every");
            pause.Click += async (_, _) =>
            {
                if (await ChangeAsync(HttpMethod.Post, TafKnob.PausePath(taf, !paused), "{}"u8.ToArray()) is { Answer: not null })
                {
                    paused = !paused;
                    ShowState();
                    ShowSetup();
                }
            };
            SetupPane.Children.Add(pause);
        }
    }

    /// <summary>
    /// Results go to, as the iPad's <c>ResultsGoToView</c>: at least one place stays on; tafs' own places, switched at once
    /// (<c>PUT …/results</c>) from the live taf's; then the places in the person's connected apps, each on or off as the dial
    /// has it, and Add a place from a connected app with Coach.
    /// </summary>
    void ShowResultsGoTo()
    {
        var w = App.Words;
        SetupScreenHead("setup.results_go_to");
        if (knob is not { } k)
        {
            KnobNotRead("setup.reading_results");
            return;
        }
        var manages = opening?.Manages == true;
        SetupPane.Children.Add(Words(w.Get("setup.at_least_one"), 13, "TafsDimText"));
        KnobPending(k);

        var live = ResultPlaces.Live(liveResults, k);
        var own = new List<UIElement> { Eyebrow("setup.from_tafs") };
        foreach (var place in ResultPlaces.Shown(w, live, opening?.Email, manages, knobBusy))
            own.Add(PlaceRow(place.Title, place.Line, place.On, place.CanSwitch, place.Place, on =>
            {
                if (live is null || ResultPlaces.Switched(live, place.Place, on) is not { } places || opening?.CloudId is not { } id) return;
                _ = SetPlacesAsync(id, places);
            }));
        SetupPane.Children.Add(Panel([.. own]));

        var apps = new List<UIElement> { Eyebrow("setup.in_apps") };
        foreach (var item in k.AppPlaces) apps.Add(PlaceRow(item.Label, item.Words, item.On, false, item.Id, null));
        if (manages && opening?.Improve is { } ask)
        {
            var add = new HyperlinkButton { Content = w.Get("setup.add_place"), Padding = new Thickness(0), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            add.Click += (_, _) => ask();
            apps.Add(add);
        }
        SetupPane.Children.Add(Panel([.. apps]));
    }

    /// <summary>A place and its switch: its title, its line under it, on in KIFF's green.</summary>
    static Grid PlaceRow(string title, string line, bool on, bool canSwitch, string id, Action<bool>? set)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var said = new StackPanel { Spacing = 2 };
        var name = Words(title, 15, "TafsInkText");
        name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        said.Children.Add(name);
        if (line.Length > 0) said.Children.Add(Words(line, 13, "TafsSoftText"));
        row.Children.Add(said);
        var toggle = new ToggleSwitch { IsOn = on, IsEnabled = canSwitch, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(toggle, title);
        AutomationProperties.SetAutomationId(toggle, "place." + id);
        // Only the person's own switch acts, once: the page drawing it again never does.
        if (set is not null) toggle.Toggled += (_, _) =>
        {
            if (toggle.IsOn == on || !toggle.IsEnabled) return;
            toggle.IsEnabled = false;
            set(toggle.IsOn);
        };
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);
        return row;
    }

    /// <summary>tafs' own places set at once; the live taf's places as tafs cloud answers them, else they stay as they were.</summary>
    async Task SetPlacesAsync(string id, IReadOnlyList<string> places)
    {
        var sent = await ChangeAsync(HttpMethod.Put, TafKnob.ResultsPath(id), ResultPlaces.Body(places));
        if (sent?.Answer is { } a) liveResults = ResultPlaces.Answered(a) ?? places;
        if (sent is not null) ShowSetup();
    }

    /// <summary>A theme colour at a fraction of its opacity, as the iPad's <c>.opacity</c>.</summary>
    Microsoft.UI.Xaml.Media.SolidColorBrush Faint(string key, double alpha)
    {
        var c = ThemeColour(key);
        c.A = (byte)Math.Round(c.A * alpha);
        return new(c);
    }
}
