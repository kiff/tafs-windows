using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Approvals;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.App;

/// <summary>
/// Trying a version, for whoever manages a tafs cloud taf on a PC whose passkey signs (the iPad's <c>VersionsPanel</c> and
/// <c>TrialStartSheet</c>): the taf's open trial read, the trial Card of a version read before Windows Hello, Start trying,
/// and the trial's chat opened.
/// </summary>
public sealed record TafTrying(Func<Task<(OpenTrial? Open, string? Why)>> Load, Func<int, Task<(AgentDraft? Draft, string? Why)>> Card,
    Func<int, string, Task<TrialStart>> Start, Action<OpenTrial> Open);

/// <summary>
/// A trial open on a taf's page (the iPad's <c>TrialScreen</c>): the version's own chat under a bar that says it isn't live,
/// with Go back and Install, each saying why when it didn't go; once either is done, back to the taf.
/// </summary>
public sealed record TafInTrial(OpenTrial Open, string Taf, Func<Task<string?>> GoBack, Func<Task<string?>> Install, Action Ended);

public sealed partial class TafPage
{
    /// <summary>The taf's open trial, read with its versions: one a taf, so Try it waits for it to end.</summary>
    OpenTrial? openTrial;
    string? trialWhy;
    /// <summary>A trial starting, installing or ending, so one tap does one thing.</summary>
    bool trialBusy;

    async Task LoadTrialAsync()
    {
        if (opening is not { Trying: { } trying } mine) return;
        var (open, why) = await trying.Load();
        if (opening != mine) return;
        (openTrial, trialWhy) = (open, why);
        if (tab == TafTab.Card && cardScreen == CardScreen.Versions) ShowCards();
    }

    /// <summary>Its tag beside "Version 2": Trying, in amber, while its trial is open.</summary>
    static Border Capsule(string words, Microsoft.UI.Xaml.Media.Brush colour) =>
        new()
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 2, 7, 2), Background = colour, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = words, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White) },
        };

    /// <summary>
    /// Under a version that isn't live, as the iPad's row: Resume the trial when it's the one being tried, else Try it, off
    /// while another trial is open. None for the live one, or for anyone who can't try one on this PC.
    /// </summary>
    StackPanel? TrialButtons(WordBook w, TafVersion v)
    {
        if (v.Live || opening?.Trying is not { } trying) return null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (openTrial is { } open && open.N == v.N)
        {
            var resume = new Button { Content = w.Get("version.resume_trial"), Style = Look("TafsOutlineButton") };
            resume.Click += (_, _) => trying.Open(open);
            row.Children.Add(resume);
        }
        else
        {
            var tryIt = new Button { Content = w.Get("version.try_it"), Style = Look("TafsOutlineButton"), IsEnabled = openTrial is null && !trialBusy };
            AutomationProperties.SetName(tryIt, w.Get("version.try_number", ("version", v.N.ToString(CultureInfo.InvariantCulture))));
            tryIt.Click += (_, _) => _ = StartTrialAsync(w, trying, v);
            row.Children.Add(tryIt);
        }
        return row;
    }

    /// <summary>
    /// Before a trial starts, as the iPad's <c>TrialStartSheet</c>: what the version changes, what trying it means for the live
    /// taf, and the Card it runs under, read from KIFF. Start trying signs that Card with one Windows Hello; a Card that changed
    /// since it was shown is shown again to read. Started, its chat opens.
    /// </summary>
    async Task StartTrialAsync(WordBook w, TafTrying trying, TafVersion v)
    {
        if (trialBusy) return;
        var mine = opening;
        var n = v.N.ToString(CultureInfo.InvariantCulture);
        AgentDraft? draft = null;
        string? why = null;
        var loaded = false;
        var body = new StackPanel { Spacing = 10 };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = w.Get("version.try_it"),
            Content = new ScrollViewer { Content = body, MaxHeight = 480 },
            PrimaryButtonText = w.Get("version.start_trying"), CloseButtonText = w.Get("common.cancel"),
            DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = false,
        };
        void Draw()
        {
            body.Children.Clear();
            var head = Words(v.Note is { Length: > 0 } note ? w.Get("version.try_number_note", ("version", n), ("note", note)) : w.Get("version.try_number", ("version", n)),
                17, "TafsInkText");
            head.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            body.Children.Add(head);
            body.Children.Add(new TextBlock { Text = w.Get("version.while_trying"), Style = Look("TafsEyebrow") });
            foreach (var line in new[]
            {
                w.Get("version.while_you_try.chat", ("n", n)), w.Get("version.while_you_try.live_keeps_working"),
                w.Get("version.while_you_try.counts_from_zero"), w.Get("version.while_you_try.ends"),
            }) body.Children.Add(Words(line, 14, "TafsInkText"));
            if (draft is { Cards: not null } shown)
            {
                body.Children.Add(new TextBlock { Text = w.Get("card.its_card"), Style = Look("TafsEyebrow") });
                foreach (var line in shown.CardLines(w).SelectMany(l => l))
                {
                    var mono = Words(line, 12, "TafsInkText");
                    mono.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas");
                    body.Children.Add(mono);
                }
            }
            else if (loaded && why is null) body.Children.Add(Words(w.Get("version.trial_card_unreadable"), 13, "TafsNoText"));
            else if (!loaded) body.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left });
            if (why is not null) body.Children.Add(Words(why, 13, "TafsNoText"));
            dialog.PrimaryButtonText = w.Get(trialBusy ? "common.starting" : "version.start_trying");
            dialog.IsPrimaryButtonEnabled = draft?.Cards is not null && !trialBusy;
        }
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            // The sheet stays while it starts; started, it closes and the chat opens.
            args.Cancel = true;
            if (trialBusy || draft is not { } read) return;
            var deferral = args.GetDeferral();
            trialBusy = true;
            why = null;
            Draw();
            try
            {
                var started = await trying.Start(v.N, read.Version);
                if (opening != mine) { dialog.Hide(); return; }
                if (started.Open is { } open)
                {
                    openTrial = open;
                    dialog.Hide();
                    trying.Open(open);
                    return;
                }
                // Changed since it was shown: the new terms, to read again.
                if (started.Changed is { } changed) draft = changed;
                why = started.Why;
            }
            finally
            {
                trialBusy = false;
                deferral.Complete();
                Draw();
            }
        };
        Draw();
        var shownTask = dialog.ShowAsync();
        (draft, why) = await trying.Card(v.N);
        loaded = true;
        Draw();
        await shownTask;
        if (opening == mine && tab == TafTab.Card && cardScreen == CardScreen.Versions) ShowCards();
    }

    /// <summary>
    /// The trial's bar, where the tabs are on a taf's page, as the iPad's <c>TrialScreen.bar</c>: "Trying version 2" and
    /// "not live" in amber; Go back, which asks first and needs no Windows Hello; Install, which asks first and signs its Card
    /// live with one; why either didn't go, under them.
    /// </summary>
    void ShowTrialBar()
    {
        if (opening?.InTrial is not { } trial) return;
        var w = App.Words;
        var n = trial.Open.N.ToString(CultureInfo.InvariantCulture);
        TrialBar.Children.Clear();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var title = Words(w.Get("version.trial.trying", ("n", n)), 15, "TafsInkText");
        title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        head.Children.Add(title);
        head.Children.Add(Capsule(w.Get("version.not_live_tag"), (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TafsAmber"]));
        TrialBar.Children.Add(head);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var back = new Button { Content = w.Get("version.go_back"), Style = Look("TafsOutlineNo"), IsEnabled = !trialBusy };
        back.Click += async (_, _) =>
        {
            var ask = new ContentDialog
            {
                XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = w.Get("version.go_back_title"),
                Content = new TextBlock { Text = w.Get("version.go_back_body", ("version", n), ("taf", trial.Taf)), TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = w.Get("version.go_back"), CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
            };
            if (await ask.ShowAsync() == ContentDialogResult.Primary) await EndTrialAsync(trial, trial.GoBack);
        };
        var install = new Button { Content = w.Get(trialBusy ? "version.working" : "version.install"), Style = Look("TafsOutlineButton"), IsEnabled = !trialBusy };
        install.Click += async (_, _) =>
        {
            var ask = new ContentDialog
            {
                XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = w.Get("version.install_title", ("version", n)),
                Content = new TextBlock { Text = w.Get("version.install_body", ("version", n)), TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = w.Get("version.install_with", ("biometry", Tafs.Core.Coach.CoachScreen.Biometry)),
                CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
            };
            if (await ask.ShowAsync() == ContentDialogResult.Primary) await EndTrialAsync(trial, trial.Install);
        };
        buttons.Children.Add(back);
        buttons.Children.Add(install);
        TrialBar.Children.Add(buttons);
        if (trialWhy is { } why) TrialBar.Children.Add(Words(why, 13, "TafsNoText"));
    }

    /// <summary>Go back or Install: done, back to the taf; not, why under the bar; Windows Hello closed, nothing said.</summary>
    async Task EndTrialAsync(TafInTrial trial, Func<Task<string?>> run)
    {
        if (trialBusy) return;
        var mine = opening;
        (trialBusy, trialWhy) = (true, null);
        ShowTrialBar();
        var why = await run();
        if (opening != mine) return;
        trialBusy = false;
        if (why is null)
        {
            trial.Ended();
            return;
        }
        trialWhy = why.Length > 0 ? why : null;
        ShowTrialBar();
    }

    /// <summary>Before the trial's first message, what it is and what to try, as the iPad's <c>TrialScreen.guide</c>.</summary>
    void AddTrialGuide(TafInTrial trial)
    {
        var guide = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 12) };
        var lines = trial.Open.Guide(App.Words, trial.Taf, TimeZoneInfo.Local);
        for (var i = 0; i < lines.Count; i++) guide.Children.Add(Words(lines[i], i == lines.Count - 1 ? 13 : 14, i == lines.Count - 1 ? "TafsSoftText" : "TafsInkText"));
        ChatPane.Children.Add(guide);
    }
}
