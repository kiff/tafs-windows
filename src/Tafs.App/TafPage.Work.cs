using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// A tafs cloud taf's Activity, above its record, as the iPad's <c>TafActivityList</c> parts: its work (S17, the iPad's
/// <c>WorkPanel</c> and <c>HealthLines</c>) and its runs (S18, <c>RunsPanel</c>): when it runs next, and Run now.
/// </summary>
public sealed partial class TafPage
{
    TafWork? work;
    string? workWhy;
    /// <summary>Run now started no run: tafs cloud's words, as they are.</summary>
    string? runNowWords;
    bool runNowGoing;
    readonly HashSet<string> sendingAgain = [];

    /// <summary>Its work, read as the Activity shows, after a change, and on Try again; a failed read keeps the last.</summary>
    async Task LoadWorkAsync()
    {
        _ = LoadRunsAsync(all: runsAll, more: false);
        if (opening is not { CloudId: { } id, Read: { } read }) return;
        var answer = await read(TafWork.Path(id));
        if (opening?.CloudId != id) return;
        if (answer.Answer is { } a && TafWork.Read(a) is { } w) (work, workWhy) = (w, null);
        else workWhy = App.Words.Get("work.not_read", ("why", answer.Why ?? ""));
        ShowWork();
    }

    /// <summary>A step answered, or answering, or Windows Hello's state changed: its work drawn again, as it was read.</summary>
    internal void StepsChanged() => ShowWork();

    /// <summary>A step answered or ended from its page: its work read again, as the iPad's <c>YourStepCard(onTaf:)</c>.</summary>
    internal void StepAnswered() => _ = LoadWorkAsync();

    /// <summary>The work and the runs, drawn into the top of the Activity, for a taf tafs cloud runs; or a run's own page.</summary>
    void ShowWork()
    {
        WorkHost.Children.Clear();
        if (opening?.CloudId is null) return;
        var w = App.Words;
        // A run's own page, in place of the Activity, with Back: what showed under it comes back as it was.
        foreach (var other in ActivityPane.Children.OfType<FrameworkElement>().Where(e => e != WorkHost))
        {
            if (runOpen is not null && other.Visibility == Visibility.Visible) { hiddenForRun.Add(other); other.Visibility = Visibility.Collapsed; }
            else if (runOpen is null && hiddenForRun.Remove(other)) other.Visibility = Visibility.Visible;
        }
        if (runOpen is { } run)
        {
            ShowRun(w, run);
            return;
        }
        if (work is not null || workWhy is not null) WorkHost.Children.Add(WorkPanel(w));
        WorkHost.Children.Add(RunsPanel(w));
        if (runPages.Count > 0 || runPagesWhy is not null) WorkHost.Children.Add(KeptRunsPanel(w));
    }

    /// <summary>
    /// Where its work stands, as tafs cloud says it: its line (unless its health's first reason says it), its health's lines
    /// and fixes, its oldest waiting case (amber past the promise), Answer within for whoever manages it, then its requests
    /// and skipped firings, newest first: one needing the owner amber, a failed one red, a skipped one dim; a reply given up
    /// on offers Send again.
    /// </summary>
    Border WorkPanel(Tafs.Core.Words.WordBook w)
    {
        var inside = new StackPanel { Spacing = 8 };
        inside.Children.Add(Eyebrow("taf.its_work"));
        var health = work?.Health ?? opening?.Health;
        if (work?.Line is { Length: > 0 } line && health?.Repeats(line) != true) inside.Children.Add(Words(line, 15, "TafsInkText"));
        if (health is { NeedsAttention: true }) inside.Children.Add(HealthLines(health));
        if (work?.Oldest is { } oldest)
        {
            var o = Words(oldest.Words, 14, oldest.Late ? "TafsAmberWords" : "TafsSoftText");
            if (oldest.Late) o.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            AutomationProperties.SetAutomationId(o, "work.oldest-waiting");
            inside.Children.Add(o);
        }
        if (work is not null && opening?.Manages == true && opening.Change is not null) inside.Children.Add(PromiseRow(w, work.PromiseHours));
        foreach (var row in work?.Rows ?? [])
        {
            if (row.Skipped is { } s) inside.Children.Add(Words(s.Words, 13, "TafsDimText"));
            else if (row.Request is { } r) inside.Children.Add(RequestRow(w, r));
        }
        if (workWhy is { } why) inside.Children.Add(Words(why, 13, "TafsNoText"));
        return Panel(inside);
    }

    /// <summary>
    /// Its health, as the iPad's <c>HealthLines</c>: each reason in amber (red when failing), each with tafs cloud's fixes:
    /// Coach opened with its words in the box (for whoever manages it and may use Coach), or Plan &amp; usage for credits.
    /// </summary>
    StackPanel HealthLines(TafHealth health)
    {
        var lines = new StackPanel { Spacing = 8 };
        AutomationProperties.SetAutomationId(lines, "taf.health");
        for (var i = 0; i < health.Lines.Count; i++)
        {
            lines.Children.Add(Words(health.Lines[i], 14, health.Failing ? "TafsNoText" : "TafsAmberWords"));
            foreach (var fix in health.FixesFor(i))
            {
                Action? act = fix.Kind switch
                {
                    "coach" when opening?.Manages == true && opening.AskCoachWith is { } ask && fix.Say is { } say => () => ask(say),
                    "credits" when opening?.OpenUsage is { } usage => usage,
                    _ => null,
                };
                if (act is null) continue;
                var b = new Button { Content = fix.Words, Style = Look("TafsOutlineButton") };
                AutomationProperties.SetAutomationId(b, "taf.health.fix." + fix.Kind);
                b.Click += (_, _) => act();
                lines.Children.Add(b);
            }
        }
        return lines;
    }

    /// <summary>Answer within: the promise as tafs cloud keeps it, or Not set, and a menu of whole hours to change or clear it.</summary>
    StackPanel PromiseRow(Tafs.Core.Words.WordBook w, int? hours)
    {
        var row = new StackPanel { Spacing = 2 };
        var menu = new MenuFlyout();
        void Pick(int? h)
        {
            if (h == hours || opening is not { CloudId: { } id, Change: { } change }) return;
            _ = SetPromiseAsync(id, change, h);
        }
        var none = new MenuFlyoutItem { Text = w.Get("work.answer_within_none") };
        none.Click += (_, _) => Pick(null);
        menu.Items.Add(none);
        foreach (var h in TafWork.PromiseChoices)
        {
            var item = new MenuFlyoutItem { Text = TafWork.PromiseWords(w, h) };
            item.Click += (_, _) => Pick(h);
            menu.Items.Add(item);
        }
        var line = new Grid { ColumnSpacing = 8 };
        line.ColumnDefinitions.Add(new ColumnDefinition());
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.Children.Add(Words(w.Get("work.answer_within"), 15, "TafsInkText"));
        var value = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        value.Children.Add(new TextBlock { Text = TafWork.PromiseWords(w, hours), FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Style = Look("TafsAmberWords") });
        value.Children.Add(new FontIcon { Glyph = "", FontSize = 11, Style = Look("TafsAmberIcon") });
        var pick = new Button { Content = value, Flyout = menu, Style = Look("TafsQuietButton"), MinHeight = 44 };
        AutomationProperties.SetName(pick, w.Get("work.answer_within") + ", " + TafWork.PromiseWords(w, hours));
        AutomationProperties.SetAutomationId(pick, "work.answer-within");
        Grid.SetColumn(pick, 1);
        line.Children.Add(pick);
        row.Children.Add(line);
        row.Children.Add(Words(w.Get("work.answer_within_line"), 12, "TafsDimText"));
        return row;
    }

    async Task SetPromiseAsync(string id, Func<HttpMethod, string, byte[]?, Task<CloudRead>> change, int? hours)
    {
        var sent = await change(HttpMethod.Put, TafWork.PromisePath(id), TafWork.PromiseBody(hours));
        if (sent.Answer is null) workWhy = App.Words.Get("work.answer_within_not_set", ("why", sent.Why ?? ""));
        await LoadWorkAsync();
    }

    /// <summary>One request: its line in its state's colour, what it used, where its result went, and Send again when given up on.</summary>
    StackPanel RequestRow(Tafs.Core.Words.WordBook w, WorkRequest r)
    {
        var row = new StackPanel { Spacing = 2 };
        // A step the taf handed its owner, answered here too, in place of its line: Home's, which carries the vendor's link
        // when tafs cloud vouched for it, else the request's own. Answered on this PC, the line again.
        if (r.Step is { } own && opening?.Steps is { } steps && !steps.Answered(own.Handoff))
        {
            var step = steps.Listed(own.Handoff) ?? own;
            row.Children.Add(StepCard.Build(step, onTaf: true, steps.CanAnswer(), steps.Answering(step.Handoff), steps.Hello(),
                why => StepCard.NotReady(why, steps.CheckHello), done => steps.Answer(step, done)));
            // Why the last answer to this step didn't go through, in red under it, as Needs you says it at its top.
            if (steps.Why(step.Handoff) is { } refused) row.Children.Add(Words(refused, 13, "TafsNoText"));
        }
        else row.Children.Add(Words(r.Line, 14, r.State == "needs_you" ? "TafsAmberWords" : r.State == "failed" ? "TafsNoText" : "TafsSoftText"));
        if (r.CreditsLine is { } used) row.Children.Add(Words(used, 12, "TafsDimText"));
        foreach (var d in r.Deliveries)
        {
            row.Children.Add(Words(d.Line, 12, d.SendAgain ? "TafsAmberWords" : "TafsDimText"));
            if (!d.SendAgain || opening?.Manages != true || opening.Change is not { } change || opening.CloudId is not { } id) continue;
            var sending = sendingAgain.Contains(r.Id);
            var again = new Button { Content = w.Get(sending ? "work.sending_again" : "work.send_again"), Style = Look("TafsOutlineButton"), IsEnabled = !sending,
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            again.Click += async (_, _) =>
            {
                if (!sendingAgain.Add(r.Id)) return;
                ShowWork();
                var sent = await change(HttpMethod.Post, TafWork.SendAgainPath(id, r.Id, d.SendPlace), "{}"u8.ToArray());
                sendingAgain.Remove(r.Id);
                if (sent.Answer is null) workWhy = w.Get("work.not_sent_again", ("why", sent.Why ?? ""));
                await LoadWorkAsync();
            };
            row.Children.Add(again);
        }
        return row;
    }

    /// <summary>
    /// Its runs, as the iPad's <c>RunsPanel</c>: that it works from its chat (no schedule), or when it runs next (paused, said so)
    /// and Run now, "Starting a run…" until tafs cloud answers; a firing that started nothing says tafs cloud's words.
    /// </summary>
    Border RunsPanel(Tafs.Core.Words.WordBook w)
    {
        var inside = new StackPanel { Spacing = 8 };
        inside.Children.Add(Eyebrow("taf.runs"));
        var starts = opening?.Starts;
        if (starts?.Unscheduled == true)
        {
            inside.Children.Add(Words(w.Get("taf.works_from_chat"), 15, "TafsInkText"));
            return Panel(inside);
        }
        inside.Children.Add(Words(paused ? w.Get("taf.state.paused")
            : starts?.NextRunWords ?? (starts?.NextRun is { } next ? w.Get("taf.next_run_at", ("time", next.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)))
            : w.Get("taf.on_its_schedule")), 15, "TafsInkText"));
        if (opening?.Manages == true && opening.Change is { } change && opening.CloudId is { } id)
        {
            var run = new Button { Content = w.Get(runNowGoing ? "taf.starting_run" : "taf.run_now"), Style = Look("TafsOutlineButton"), IsEnabled = !runNowGoing,
                HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetAutomationId(run, "taf.run-now");
            run.Click += async (_, _) =>
            {
                if (runNowGoing) return;
                (runNowGoing, runNowWords) = (true, null);
                ShowWork();
                var sent = await change(HttpMethod.Post, "/v1/tafs/" + CloudRead.PathPart(id) + "/run", "{}"u8.ToArray());
                runNowGoing = false;
                runNowWords = sent.Answer is { } a ? TafRunNow.Words(a) : sent.Why;
                ShowWork();
                _ = LoadActivityAsync();
                _ = LoadWorkAsync();
            };
            inside.Children.Add(run);
            if (runNowWords is { } said) inside.Children.Add(Words(said, 13, "TafsSoftText"));
        }
        return Panel(inside);
    }
}
