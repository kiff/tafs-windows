using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Approvals;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.App;

/// <summary>
/// Install this version, for whoever manages a tafs cloud taf on a PC whose passkey signs (the iPad's <c>prepareSwitch</c>,
/// <c>VersionCardSheet</c> and <c>useVersion</c>): the switch begun, the Card it waits for read, the install, and Not now.
/// </summary>
public sealed record TafInstalling(Func<int, bool, bool, Task<SwitchBegun>> Begin, Func<string, Task<(AgentDraft? Draft, string? Why)>> Card,
    Func<int, AgentDraft?, Task<SwitchResult>> Install, Func<Task<string?>> Cancel);

public sealed partial class TafPage
{
    /// <summary>Why the last install didn't go, under the versions.</summary>
    string? installWhy;
    /// <summary>A version whose Card KIFF said moved on since it was drafted: "Draft its Card again".</summary>
    int? staleVersion;
    /// <summary>A version a run of the taf is in the way of: "Stop it and install".</summary>
    int? blockedVersion;
    /// <summary>The version installing now: one switch at a time.</summary>
    int? installing;

    /// <summary>
    /// Install this version, under a version that isn't live, as the iPad's row: off while another switch is under way or a
    /// trial is open (Install ends a trial), and "Installing…" while it goes.
    /// </summary>
    Button? InstallButton(WordBook w, TafVersion v)
    {
        if (v.Live || opening?.Installing is null) return null;
        var install = new Button
        {
            Content = w.Get(installing == v.N ? "version.installing" : "version.install_this"), Style = Look("TafsOutlineButton"),
            IsEnabled = installing is null && openTrial is null && (versions?.Switching is null || versions.Switching == v.N),
        };
        AutomationProperties.SetName(install, w.Get("version.install_this") + ", " + w.Get("version.number", ("version", v.N.ToString(CultureInfo.InvariantCulture))));
        install.Click += (_, _) => _ = UseVersionAsync(v);
        return install;
    }

    /// <summary>
    /// Under the versions: a switch under way ("Installing version 3", that the taf doesn't run until it's done, Finish and
    /// Cancel), Draft its Card again, Stop it and install, and why the last install didn't go.
    /// </summary>
    void AddInstallState(WordBook w, StackPanel inside)
    {
        if (opening?.Installing is not { } calls || versions is not { } list) return;
        if (list.Switching is { } n && list.Versions.FirstOrDefault(x => x.N == n) is { } under)
        {
            inside.Children.Add(Words(w.Get("version.doesnt_run_until"), 13, "TafsSoftText"));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var finish = new Button { Content = w.Get(installing == n ? "version.installing" : "version.finish"), Style = Look("TafsOutlineButton"), IsEnabled = installing is null };
            finish.Click += (_, _) => _ = UseVersionAsync(under);
            var cancel = new Button { Content = w.Get("common.cancel"), Style = Look("TafsOutlineNo"), IsEnabled = installing is null };
            cancel.Click += async (_, _) =>
            {
                installWhy = await calls.Cancel();
                await LoadVersionsAsync();
            };
            row.Children.Add(finish);
            row.Children.Add(cancel);
            inside.Children.Add(row);
        }
        if (staleVersion is { } stale && list.Versions.FirstOrDefault(x => x.N == stale) is { } again)
        {
            var redraft = new Button { Content = w.Get("version.redraft"), Style = Look("TafsOutlineButton"), IsEnabled = installing is null };
            redraft.Click += (_, _) => _ = UseVersionAsync(again, redraft: true);
            inside.Children.Add(redraft);
        }
        if (blockedVersion is { } blocked && list.Versions.FirstOrDefault(x => x.N == blocked) is { } waiting)
        {
            // One step: what's in the way stops (a held call withdrawn, nothing sent), then the install begins.
            var stop = new Button { Content = w.Get("version.stop_and_install"), Style = Look("TafsOutlineButton"), IsEnabled = installing is null };
            stop.Click += (_, _) => _ = UseVersionAsync(waiting, stopBlocking: true);
            inside.Children.Add(stop);
        }
        if (installWhy is { } why) inside.Children.Add(Words(why, 13, "TafsNoText"));
    }

    /// <summary>
    /// Install this version, as the iPad's <c>use</c>: the switch begins (from here the taf doesn't run); with a Card, it's read
    /// before Windows Hello; without, one Windows Hello makes it live. Done, the versions and the dial are read again.
    /// </summary>
    async Task UseVersionAsync(TafVersion v, bool redraft = false, bool stopBlocking = false)
    {
        if (installing is not null || opening?.Installing is not { } calls) return;
        var mine = opening;
        (installing, installWhy) = (v.N, null);
        ShowVersionsAgain();
        try
        {
            var begun = await calls.Begin(v.N, redraft, stopBlocking);
            if (opening != mine) return;
            if (begun.Switch is not { } sw)
            {
                (installWhy, blockedVersion) = (begun.Why, begun.Blocked ? v.N : null);
                return;
            }
            (staleVersion, blockedVersion) = (null, null);
            // What the switch stopped while it waited on the owner's answer, in tafs cloud's words: said here, and in the chat
            // with Retry on that task's message (one click, never by itself).
            if (begun.StoppedWords is { } stoppedWords)
            {
                (installWhy, switchStopped) = (stoppedWords, (stoppedWords, begun.Stopped));
                if (tab == TafTab.Chat) ShowChat();
            }
            if (sw.Draft is null) Settle(await calls.Install(v.N, null), v);
            else
            {
                installing = null;
                await ReadVersionCardAsync(v, sw.Draft, null);
            }
        }
        finally
        {
            if (opening == mine)
            {
                installing = null;
                await LoadVersionsAsync();
                if (knob is not null) await LoadKnobAsync();
            }
        }
    }

    /// <summary>What the last switch stopped (tafs cloud's words, and the tasks), for the chat's Retry; none once it's sent or gone.</summary>
    (string Words, IReadOnlyList<StoppedRun> Runs)? switchStopped;

    /// <summary>Retry, for a task the switch stopped: its message sent again as a new one, once, on the owner's click.</summary>
    async Task RetryStoppedAsync(string text)
    {
        switchStopped = null;
        ShowChat();
        await SendTextAsync(text);
    }

    /// <summary>What an install came to: live, the Card shown again, Draft its Card again, or why.</summary>
    void Settle(SwitchResult result, TafVersion v)
    {
        if (result.Stale) staleVersion = v.N;
        installWhy = result.Why;
    }

    /// <summary>
    /// The Card a version brings, as the owner reads it before Windows Hello (the iPad's <c>VersionCardSheet</c>): "Version 3:
    /// what changed", each Card's lines, and that the taf doesn't run while it switches. Install this version signs exactly what
    /// was shown; Cancel drops the switch, and the taf runs as it was. A Card that changed meanwhile opens again, to read anew.
    /// </summary>
    async Task ReadVersionCardAsync(TafVersion v, string draftId, AgentDraft? changed)
    {
        if (opening?.Installing is not { } calls) return;
        var w = App.Words;
        var n = v.N.ToString(CultureInfo.InvariantCulture);
        var (draft, why) = changed is not null ? (changed, null) : await calls.Card(draftId);
        var body = new StackPanel { Spacing = 10 };
        var head = Words(v.Note is { Length: > 0 } note ? w.Get("version.number_note", ("version", n), ("note", note)) : w.Get("version.number", ("version", n)), 17, "TafsInkText");
        head.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        body.Children.Add(head);
        if (draft is { Cards: not null })
        {
            foreach (var line in draft.CardLines(w).SelectMany(l => l))
            {
                var mono = Words(line, 12, "TafsInkText");
                mono.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas");
                body.Children.Add(mono);
            }
            body.Children.Add(Words(w.Get(draft.Retires is { Count: > 0 } ? "version.card_note.removes_biometry" : "version.card_note.issues_biometry",
                ("biometry", Tafs.Core.Coach.CoachScreen.Biometry)), 12, "TafsSoftText"));
        }
        else body.Children.Add(Words(why ?? w.Get("version.card_unreadable"), 13, "TafsNoText"));
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = w.Get("card.its_card"),
            Content = new ScrollViewer { Content = body, MaxHeight = 480 },
            PrimaryButtonText = w.Get("version.install_this"), CloseButtonText = w.Get("common.cancel"),
            DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = draft?.Cards is not null,
        };
        ApprovalGate.Notice(dialog);
        var mine = opening;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            // Not now: the switch it began is dropped, and the taf runs as it was.
            installWhy = await calls.Cancel();
            return;
        }
        if (opening != mine) return;
        installing = v.N;
        ShowVersionsAgain();
        // Exactly the draft shown, by its id and version.
        var result = await calls.Install(v.N, draft);
        if (opening != mine) return;
        installing = null;
        Settle(result, v);
        // The terms changed between the screen and the click: shown again, to read anew.
        if (result.Changed is { } fresh) await ReadVersionCardAsync(v, draftId, fresh);
    }

    void ShowVersionsAgain()
    {
        if (tab == TafTab.Card && cardScreen == CardScreen.Versions) ShowCards();
        if (tab == TafTab.Setup && setupScreen != SetupScreen.Main) ShowSetup();
    }

    /// <summary>
    /// Make it live, on the dial's waiting version, as the iPad's <c>KnobPendingPanel.confirm</c>: the versions and the dial read
    /// again, then the same install as Versions', to that version. Not offered on a PC that can't sign it.
    /// </summary>
    Button? MakeLiveButton(WordBook w, KnobPending pending)
    {
        if (opening?.Installing is null) return null;
        var live = new Button
        {
            Content = installing is null ? w.Count("knob.make_live", pending.Changes, ("biometry", Tafs.Core.Coach.CoachScreen.Biometry)) : w.Get("common.confirming"),
            Style = Look("TafsPrimary"), IsEnabled = installing is null, HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(live, "setup.makeLive");
        live.Click += async (_, _) =>
        {
            await LoadVersionsAsync();
            await LoadKnobAsync();
            if (knob?.Pending?.Version != pending.Version || versions?.Versions.FirstOrDefault(v => v.N == pending.Version) is not { } version) return;
            await UseVersionAsync(version);
        };
        return live;
    }
}
