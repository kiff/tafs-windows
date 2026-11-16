using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// The runs a taf kept (the iPad's <c>RetainedRuns</c>), under its runs on Activity: tafs cloud's title for them, the latest
/// three, and "see them all" in tafs cloud's words, which lists every page kept, 20 at a time, with tafs cloud's end words.
/// Each run opens its own page (S19, the iPad's <c>KnobRequestDetail</c>): where it came from, its line, each step, and Try
/// the reply now for whoever manages the taf when its reply was given up on.
/// </summary>
public sealed partial class TafPage
{
    readonly List<RunPage> runPages = [];
    string? runPagesWhy;
    bool runsAll;
    bool runsLoading;
    string? runOpen;
    RunDetail? runDetail;
    string? runWhy;
    bool runSending;
    readonly List<FrameworkElement> hiddenForRun = [];

    /// <summary>The first page (3, or 20 when seeing them all), or the next one; a page for another taf or view is dropped.</summary>
    async Task LoadRunsAsync(bool all, bool more)
    {
        if (opening is not { CloudId: { } id, Read: { } read } || runsLoading) return;
        if (more && runPages.LastOrDefault()?.NextCursor is null) return;
        runsLoading = true;
        var answer = await read(RunPage.Path(id, all ? 20 : 3, more ? runPages[^1].NextCursor : null));
        runsLoading = false;
        if (opening?.CloudId != id || all != runsAll) return;
        if (answer.Answer is { } a && RunPage.Read(a) is { } page)
        {
            if (!more) runPages.Clear();
            runPages.Add(page);
            runPagesWhy = null;
        }
        else runPagesWhy = answer.Why ?? App.Words.Get("kiff.kiff_did_not_answer_expected");
        ShowWork();
    }

    Border KeptRunsPanel(Tafs.Core.Words.WordBook w)
    {
        var inside = new StackPanel { Spacing = 10 };
        inside.Children.Add(new TextBlock { Text = (runPages.FirstOrDefault()?.Title ?? w.Get("taf.its_work")).ToUpper(new System.Globalization.CultureInfo(w.Language)),
            Style = Look("TafsEyebrow") });
        for (var i = 0; i < runPages.Count; i++)
            foreach (var row in runPages[i].Rows(first: i == 0))
            {
                if (row.Skipped is { } s) { inside.Children.Add(Words(s.Words, 13, "TafsDimText")); continue; }
                if (row.Request is not { } r) continue;
                var lines = new StackPanel { Spacing = 4 };
                lines.Children.Add(Words(r.Line, 14, "TafsInkText"));
                if (r.CreditsLine is { } used) lines.Children.Add(Words(used, 12, "TafsDimText"));
                foreach (var d in r.Deliveries) lines.Children.Add(Words(d.Line, 12, "TafsDimText"));
                var open = new Button { Content = lines, Style = Look("TafsBox"), HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 2, 0, 2) };
                AutomationProperties.SetName(open, r.Line);
                open.Click += (_, _) => OpenRun(r.Id);
                inside.Children.Add(open);
            }
        if (runPagesWhy is { } why)
        {
            inside.Children.Add(Words(why, 13, "TafsNoText"));
            var again = new Button { Content = w.Get("common.try_again"), Style = Look("TafsOutlineButton") };
            again.Click += (_, _) => _ = LoadRunsAsync(runsAll, more: false);
            inside.Children.Add(again);
        }
        if (runsAll && runPages.LastOrDefault()?.NextCursor is not null && runPagesWhy is null)
        {
            var more = new Button { Content = w.Get(runsLoading ? "common.loading" : "common.more"), Style = Look("TafsOutlineButton"), IsEnabled = !runsLoading,
                HorizontalAlignment = HorizontalAlignment.Left };
            more.Click += (_, _) => _ = LoadRunsAsync(all: true, more: true);
            inside.Children.Add(more);
        }
        else if (!runsAll && runPages.FirstOrDefault()?.MoreTitle is { } seeAll)
        {
            var all = new HyperlinkButton { Content = seeAll, Padding = new Thickness(0), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            all.Click += (_, _) => { runsAll = true; runPages.Clear(); _ = LoadRunsAsync(all: true, more: false); };
            inside.Children.Add(all);
        }
        else if (runsAll && runPagesWhy is null && runPages.LastOrDefault()?.EndWords is { } end) inside.Children.Add(Words(end, 13, "TafsDimText"));
        return Panel(inside);
    }

    void OpenRun(string run)
    {
        (runOpen, runDetail, runWhy) = (run, null, null);
        ShowWork();
        _ = LoadRunAsync(run);
    }

    async Task LoadRunAsync(string run)
    {
        if (opening is not { CloudId: { } id, Read: { } read }) return;
        var answer = await read(RunDetail.Path(id, run));
        if (opening?.CloudId != id || runOpen != run) return;
        if (answer.Answer is { } a && RunDetail.Read(a) is { } d) (runDetail, runWhy) = (d, null);
        else runWhy = answer.Why ?? App.Words.Get("kiff.kiff_did_not_answer_expected");
        ShowWork();
    }

    /// <summary>A run's own page: Back, its line under where it came from, each step with its mark, Try the reply now, Ask Coach.</summary>
    void ShowRun(Tafs.Core.Words.WordBook w, string run)
    {
        var back = new HyperlinkButton { Padding = new Thickness(0) };
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        label.Children.Add(new FontIcon { Glyph = "", FontSize = 12 });
        label.Children.Add(new TextBlock { Text = w.Get("taf.section.activity") });
        back.Content = label;
        AutomationProperties.SetName(back, w.Get("common.back"));
        back.Click += (_, _) => { runOpen = null; ShowWork(); };
        WorkHost.Children.Add(back);
        var title = Words(w.Get("knob.request_title"), 22, "TafsInkText");
        title.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        WorkHost.Children.Add(title);
        if (runDetail is { } d)
        {
            var from = new TextBlock { Text = d.From.ToUpper(new System.Globalization.CultureInfo(w.Language)), Style = Look("TafsEyebrow") };
            WorkHost.Children.Add(Panel(from, Words(d.Line, 15, "TafsInkText")));
            var steps = new StackPanel { Spacing = 12 };
            foreach (var step in d.Steps)
            {
                var row = new Grid { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var done = step.State == "done";
                row.Children.Add(new FontIcon { Glyph = done ? "" : step.State == "next" ? "" : "", FontSize = 16, VerticalAlignment = VerticalAlignment.Top,
                    Style = Look(done ? "TafsOkIcon" : "TafsAmberIcon") });
                var words = new StackPanel { Spacing = 4 };
                var name = Words(step.Label, 15, "TafsInkText");
                name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                words.Children.Add(name);
                if (step.Line is { } line) words.Children.Add(Words(line, 13, "TafsDimText"));
                Grid.SetColumn(words, 1);
                row.Children.Add(words);
                steps.Children.Add(row);
            }
            WorkHost.Children.Add(Panel(steps));
            foreach (var delivery in d.Deliveries.Where(x => x.SendAgain))
            {
                if (opening?.Manages != true || opening.Change is not { } change || opening.CloudId is not { } id) break;
                var again = new Button { Content = w.Get(runSending ? "work.sending_again" : "work.request.try_reply_now"), Style = Look("TafsOutlineButton"),
                    IsEnabled = !runSending, HorizontalAlignment = HorizontalAlignment.Left };
                again.Click += async (_, _) =>
                {
                    runSending = true;
                    ShowWork();
                    var sent = await change(HttpMethod.Post, TafWork.SendAgainPath(id, run, delivery.SendPlace), "{}"u8.ToArray());
                    runSending = false;
                    if (sent.Answer is null) runWhy = w.Get("work.not_sent_again", ("why", sent.Why ?? ""));
                    await LoadRunAsync(run);
                };
                WorkHost.Children.Add(again);
            }
            if (opening?.AskCoach is { } ask)
            {
                var coach = new Button { Content = w.Get("coach.ask"), Style = Look("TafsOutlineButton"), HorizontalAlignment = HorizontalAlignment.Left };
                coach.Click += (_, _) => ask();
                WorkHost.Children.Add(coach);
            }
        }
        else if (runWhy is null) WorkHost.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left });
        if (runWhy is { } why)
        {
            WorkHost.Children.Add(Words(why, 13, "TafsNoText"));
            var again = new Button { Content = w.Get("common.try_again"), Style = Look("TafsOutlineButton") };
            again.Click += (_, _) => _ = LoadRunAsync(run);
            WorkHost.Children.Add(again);
        }
    }
}
