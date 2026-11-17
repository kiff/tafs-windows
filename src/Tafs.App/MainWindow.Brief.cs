using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Missions;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// Your brief, as the iPad's (Missions design v2.1): read with Home's list from tafs cloud's <c>GET /v1/brief</c>; once it
/// has something in it, it takes Needs you's place in the sidebar under tafs cloud's name for it ("Your brief"), and Needs
/// you stays one click away under it. A 404, an error, or a brief with nothing in it keeps Needs you as today.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The brief as last read; none keeps Needs you.</summary>
    Brief? brief;

    /// <summary>The label the brief is narrowed to; none is All.</summary>
    string? briefLabel;

    /// <summary>Needs you was opened from the brief: its row shows Needs you until another screen is chosen.</summary>
    bool needsYouUnderBrief;

    /// <summary>KIFF lets this person use Coach: the next move's Start opens it.</summary>
    bool mayUseCoach;

    /// <summary>The brief, with each read of Home's list; one that lands after another session began is dropped.</summary>
    async Task ReadBriefAsync(Func<string, Task<CloudRead>> read, int reading)
    {
        var answer = (await read(Brief.Path(briefLabel))).Answer;
        if (reading != sessions) return;
        brief = answer is { } json && Brief.Read(json) is { Empty: false } read2 ? read2 : null;
        if (needsYouRow is { } row) ShowCount(row, App.Words, NeedsYouTitle);
        // On the brief: drawn again with what was read; the brief gone, Needs you in its place.
        if (ReferenceEquals(Nav.SelectedItem, needsYouRow) && !needsYouUnderBrief && (Pages.Content is BriefPage) != (brief is not null)) ShowNeedsYouOrBrief();
    }

    /// <summary>The sidebar row's name: tafs cloud's for the brief once there is one, else Needs you.</summary>
    string NeedsYouTitle => brief is { Words.Tab.Length: > 0 } b ? b.Words.Tab : needsYouTitle;

    /// <summary>The row chosen: the brief when there is one, else Needs you, as the iPad's tab.</summary>
    void ShowNeedsYouOrBrief()
    {
        if (brief is { } b && !needsYouUnderBrief) Pages.Navigate(typeof(BriefPage), BriefOpeningOf(b));
        else ShowNeedsYou();
    }

    /// <summary>Needs you, its steps and unproven calls, and the hold asked for, as before the brief.</summary>
    void ShowNeedsYou()
    {
        Pages.Navigate(typeof(NeedsYouPage), held);
        if (Pages.Content is NeedsYouPage page) page.ShowUnproven(unproven, mayAnswerHolds);
        ShowSteps();
        ShowCoachWaiting();
        _ = ReadUnprovenAsync(sessions);
        ShowRequestedHold();
    }

    BriefOpening BriefOpeningOf(Brief b)
    {
        var w = App.Words;
        // What Needs you holds that the brief doesn't show, matched item by item: its held calls and steps by their refs,
        // and every unproven call, which the brief can't carry.
        var items = held.Where(h => h.KiffId is not null).Select(h => $"hold:{h.KiffId}").Concat(listedSteps.Select(s => $"step:{s.Handoff}"));
        var beyond = Brief.NeedsYouBeyond(items, unproven.Count, b.Decisions);
        return new BriefOpening(b, briefLabel, beyond > 0 ? w.Count("needs_you.count", beyond) : null,
            Filter: async label =>
            {
                if (cloudRead is not { } read) return null;
                var reading = sessions;
                var answer = (await read(Brief.Path(label))).Answer;
                if (reading != sessions || answer is not { } json || Brief.Read(json) is not { } narrowed) return null;
                (briefLabel, brief) = (label, narrowed);
                return narrowed;
            },
            Answer: OpenDecision,
            Later: async d =>
            {
                if (cloudSend is not { } send) return w.Get("error.offline");
                var r = await send(Brief.LaterPath(d.Id), "{}"u8.ToArray());
                return r.Answer is not null ? null : r.Why ?? w.Get("error.ours");
            },
            Log: async mission =>
                cloudRead is { } read && (await read(MissionLog.Path(mission))).Answer is { } json ? MissionLog.Read(w, json) : null,
            // Start opens Coach for someone who may use it, with the move's words in its box, as the iPad's startNextMove.
            StartMove: mayUseCoach ? move => CoachMode.Write(move.Words, newConversation: false) : null,
            OpenNeedsYou: () =>
            {
                needsYouUnderBrief = true;
                ShowNeedsYou();
            },
            TafOf: id => home?.Tafs.FirstOrDefault(t => t.Id == id) is { } taf ? (taf.Name, taf.Colour) : null);
    }

    /// <summary>
    /// A decision's answer, as the iPad's: it opens where that decision is answered, with nothing chosen and nothing signed
    /// here. A held call on its own card in Needs you, a step in Needs you, a taf whose checks fail on its page.
    /// </summary>
    void OpenDecision(BriefDecision decision)
    {
        var to = Brief.Destination(decision);
        if (to.Goes == BriefGoes.Taf && to.Id is { } taf && home?.Tafs.Any(t => t.Id == taf) == true)
        {
            // Selected in the sidebar when it's listed there, as the iPad's; else its page alone.
            if (Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag is TafRow r && r.Key == taf) is { } row) Nav.SelectedItem = row;
            else Pages.Navigate(typeof(TafPage), OpenTaf(App.Words, taf));
            return;
        }
        if (to.Goes == BriefGoes.Hold) requestedHold = to.Id;
        needsYouUnderBrief = true;
        ShowNeedsYou();
    }
}
