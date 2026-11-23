using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Coach;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// Coach's drafted door "From another application" (<c>show {type: "door", taf_id, draft_id}</c>), as the iPad's
/// <c>DoorDraftCard</c>: drawn from tafs cloud's draft, never from Coach's words. The application's name, what it may do,
/// what the taf does, the fields it sends and what it gets back, its own words' risk when it's on, and the boundary.
/// Approve signs the draft's exact canonical body with one Windows Hello and opens the door; its first key is then shown
/// once. Only for whoever manages the taf, on a PC that approves with its passkey.
/// </summary>
public sealed partial class CoachPanel
{
    /// <summary>A taf's door signing, by its tafs cloud id: none for anyone who doesn't manage it, or a PC that can't approve.</summary>
    public Func<string, TafDoorSigning?>? DoorSigning { get; set; }

    /// <summary>What each door card read and did, by the card's id, so a redraw keeps it.</summary>
    sealed class DoorCardState
    {
        public DoorDraft? Draft;
        public string? Said;
        public bool Used, Busy, Approved, Reading;
    }

    readonly Dictionary<string, DoorCardState> doorCards = [];

    Border DoorCard(CoachCard c)
    {
        var w = App.Words;
        var state = doorCards.TryGetValue(c.Id, out var known) ? known : doorCards[c.Id] = new DoorCardState();
        var lines = new StackPanel { Spacing = 10 };
        lines.Children.Add(new TextBlock { Text = w.Get("door.api.draft_kind"), Style = (Style)Application.Current.Resources["TafsEyebrow"] });
        var tafName = c.TafId is { } tid ? Tafs?.Invoke().FirstOrDefault(t => t.Id == tid)?.Name ?? "" : "";
        if (state.Draft is { } d)
        {
            var name = Words(d.Name, 17, "TafsInkText");
            name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            lines.Children.Add(name);
            lines.Children.Add(Words(w.Get("door.api.draft_scope", ("app", d.Name), ("taf", tafName)), 14, "TafsSoftText"));
            lines.Children.Add(Words(w.Get("door.api.draft_will", ("taf", tafName), ("job", d.Job)), 15, "TafsInkText"));
            if (d.Fields.Count > 0)
            {
                lines.Children.Add(new TextBlock { Text = w.Get("door.api.draft_sends"), Style = (Style)Application.Current.Resources["TafsEyebrow"] });
                foreach (var f in d.Fields)
                {
                    var field = Words(ApiDoorDrafts.Field(f), 13, "TafsInkText");
                    field.FontFamily = Monospace.Face;
                    field.FlowDirection = FlowDirection.LeftToRight;
                    lines.Children.Add(field);
                }
            }
            if (d.Outcomes.Count > 0) lines.Children.Add(Words(ApiDoorDrafts.OutcomesLine(w, d), 14, "TafsInkText"));
            if (d.Text)
            {
                var own = Words(w.Get("door.api.draft_words", ("taf", tafName)), 14, "TafsInkText");
                own.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                lines.Children.Add(own);
                lines.Children.Add(Words(w.Get("door.api.draft_words_risk", ("taf", tafName), ("app", d.Name)), 13, "TafsAmberWords"));
            }
            lines.Children.Add(Words(w.Get("door.api.boundary", ("taf", tafName)), 13, "TafsSoftText"));
            if (state.Approved)
            {
                var open = Words(w.Get("door.status.open"), 14, "TafsOkText");
                open.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                lines.Children.Add(open);
            }
            else if (c.TafId is { } taf && DoorSigning?.Invoke(taf) is { } signing)
            {
                var approve = new Button
                {
                    Content = w.Get(state.Busy ? "common.confirming" : "approval.approve"), IsEnabled = !state.Busy,
                    Style = (Style)Application.Current.Resources[state.Busy ? "TafsPrimaryOff" : "TafsPrimary"], Padding = new Thickness(18, 8, 18, 8),
                };
                AutomationProperties.SetAutomationId(approve, "door.draft.approve");
                approve.Click += (_, _) => _ = ApproveDoorAsync(c, taf, d, tafName, state, signing);
                lines.Children.Add(approve);
            }
        }
        else if (state.Said is null)
        {
            lines.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left });
            if (!state.Reading) _ = ReadDoorDraftAsync(c, state);
        }
        if (state.Said is { } said)
        {
            // A draft a door was already made from (410 draft_used) is tafs cloud's words in the ok colour: nothing went wrong.
            var line = Words(said, 13, state.Used ? "TafsOkText" : "TafsNoText");
            AutomationProperties.SetAutomationId(line, state.Used ? "door.draft.used" : "door.draft.failed");
            lines.Children.Add(line);
        }
        return new Border { Child = lines, Padding = new Thickness(16), CornerRadius = new CornerRadius(14), Background = Brush("TafsPanel"),
            BorderBrush = Brush("TafsLine"), BorderThickness = new Thickness(1) };
    }

    async Task ReadDoorDraftAsync(CoachCard c, DoorCardState state)
    {
        if (c.TafId is not { } taf || c.DraftId is not { } draft || Read is not { } read)
        {
            state.Said = App.Words.Get("error.ours");
            return;
        }
        state.Reading = true;
        var answer = await read(ApiDoorDrafts.DraftPath(taf, draft));
        state.Reading = false;
        if (answer.Answer is { } a && ApiDoorDrafts.Read(a) is { } d) (state.Draft, state.Said, state.Used) = (d, null, false);
        else (state.Used, state.Said) = (ApiDoorDrafts.Used(answer), answer.Words ?? answer.Why ?? App.Words.Get("error.ours"));
        Redraw();
    }

    /// <summary>Approve: the draft's exact canonical body signed with one Windows Hello and posted; the door's first key shown once.</summary>
    async Task ApproveDoorAsync(CoachCard c, string taf, DoorDraft d, string tafName, DoorCardState state, TafDoorSigning signing)
    {
        if (state.Busy) return;
        state.Busy = true;
        Redraw();
        var sent = await signing.Send(HttpMethod.Post, TafDoors.Path(taf), d.Body, App.Words.Get("door.api.faceid_body", ("app", d.Name), ("taf", tafName)));
        state.Busy = false;
        if (sent is null) { Redraw(); return; }
        if (sent.Answer is { } a && ApiDoorDrafts.ReadAdded(a) is { } added)
        {
            (state.Approved, state.Said) = (true, null);
            Redraw();
            await ApiDoorDialogs.ShowKeyOnceAsync(XamlRoot, ActualTheme, added.Key, added.Door, signing.Cloud);
            return;
        }
        state.Said = sent.Words ?? sent.Why ?? App.Words.Get("error.ours");
        Redraw();
    }
}
