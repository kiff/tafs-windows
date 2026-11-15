using Tafs.Core.Approvals;
using Tafs.Core.Kiff;
using Tafs.Core.Words;

namespace Tafs.Core.Coach;

/// <summary>Where an approval card stands, as the iPad's <c>CardState</c> without its <c>working</c> (nothing approves here yet).</summary>
public enum ApprovalState { NeedsYou, Done, Declined, Failed }

/// <summary>
/// Coach's approval card, as the iPad's <c>SetupApproval</c>: planque's own words for the connection, KIFF's draft for the
/// taf's Cards, and where it stands. What it says is worked out here; the panel draws it.
/// </summary>
public static class ApprovalCard
{
    /// <summary>
    /// Where it stands, as the iPad's <c>setupCardState</c>: done only by its answer in the thread (approved, or already
    /// approved), never by planque's state alone, unless planque failed the grant since; declined; failed, with why; a grant
    /// planque failed or disconnected, with planque's reason; else it needs the person.
    /// </summary>
    public static (ApprovalState State, string? Why) State(CoachThread thread, CoachCard c, PlanqueGrant? grant)
    {
        var answer = thread.AnswerTo(c.Id);
        var grantFailed = grant is { Status: "failed" or "disconnected" };
        if (!grantFailed && answer is { Failed: false } a && (a.Text.StartsWith("approved", StringComparison.Ordinal) || a.Text.StartsWith("already approved", StringComparison.Ordinal)))
            return (ApprovalState.Done, null);
        if (answer?.Text is "declined" or "not now") return (ApprovalState.Declined, null);
        if (answer is { Failed: true } f) return (ApprovalState.Failed, f.Text.StartsWith("failed: ", StringComparison.Ordinal) ? f.Text["failed: ".Length..] : null);
        if (grantFailed) return (ApprovalState.Failed, grant!.Reason);
        return (ApprovalState.NeedsYou, null);
    }

    /// <summary>A newer approval in the thread names the same connection, as the iPad's <c>replaced</c>: this one is history.</summary>
    public static bool Replaced(CoachThread thread, string id)
    {
        if (thread.Shown.FirstOrDefault(s => s.Card.Id == id) is not { Card.Grant: { } grant } shown) return false;
        return thread.Shown.Any(o => o.Card.Id != id && o.Card.Kind == "approval" && o.Card.Grant == grant && o.FirstAt > shown.FirstAt);
    }

    /// <summary>What a replaced card says, folded, as the iPad's <c>replacedLine</c>: what was done with it, then where to look.</summary>
    public static string ReplacedLine(WordBook w, CoachThread thread, string id) => thread.AnswerTo(id) switch
    {
        { Failed: false } a when a.Text.StartsWith("approved", StringComparison.Ordinal) => w.Get("coach.answer.approved_more_below"),
        { Text: "declined" } => w.Get("coach.answer.declined_again_below"),
        _ => w.Get("coach.setup_changed_request_see_below"),
    };

    /// <summary>
    /// An answered approval in one line, as the iPad's <c>outcome</c>: a Card KIFF says it signed, named first; Coach's title
    /// for the card; the draft's Card; else the vendor ("Approved · Stripe is connected"). Signed earlier, "Already approved · …".
    /// </summary>
    public static string Outcome(WordBook w, CoachThread thread, CoachCard c, ApprovalState state, PlanqueGrant? grant, AgentDraft? draft)
    {
        var signed = state == ApprovalState.Done && draft is { Signed: true, NoTools: not true } ? draft.CardTitle(w) : null;
        var title = c.Title?.Trim() is { Length: > 0 } t ? t : null;
        var vendor = (grant?.Vendor ?? c.Service) is { } v ? VendorWords(v) : w.Get("coach.the_vendor");
        if (state == ApprovalState.Done && thread.AnswerTo(c.Id)?.Text.StartsWith("already approved", StringComparison.Ordinal) == true)
            return $"{w.Get("coach.already_approved")} · {signed ?? title ?? vendor}";
        if (signed is not null) return w.Get("coach.answer.approved_title", ("title", signed));
        var declined = state == ApprovalState.Declined;
        string Titled(string what) => w.Get(declined ? "coach.answer.declined_title" : "coach.answer.approved_title", ("title", what));
        if (title is not null) return Titled(title);
        if (draft is { NoTools: not true }) return Titled(draft.CardTitle(w));
        if (declined) return Titled(vendor);
        return grant?.Connected == true ? w.Get("coach.answer.approved_connected", ("vendor", vendor)) : w.Get("coach.approved_signed", ("vendor", vendor));
    }

    /// <summary>
    /// tafs cloud's words for a Card approved next, on a card with no draft (an app's first sign-in), as the iPad's
    /// <c>cardNextWords</c>: none when its mark says the Card is signed now, or the connection is already done.
    /// </summary>
    public static string? CardNextWords(CoachCard c) =>
        c.Draft is null && c.Signs?.Card != true && c.ConnectionDone is null ? c.Signs?.Words : null;

    /// <summary>A vendor's host in words, as the iPad's <c>vendorWords</c>: "mcp.stripe.com" reads "Stripe".</summary>
    public static string VendorWords(string vendor)
    {
        var v = vendor.ToLowerInvariant();
        if (v.StartsWith("mcp.", StringComparison.Ordinal)) v = v[4..];
        foreach (var suffix in new[] { "-mcp", "_mcp", ".mcp" })
            if (v.EndsWith(suffix, StringComparison.Ordinal)) v = v[..^suffix.Length];
        if (v.IndexOf('.') is var dot and >= 0) v = v[..dot];
        return v.Length == 0 ? vendor : char.ToUpperInvariant(v[0]) + v[1..].Replace('-', ' ');
    }
}
