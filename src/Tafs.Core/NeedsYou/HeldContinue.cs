using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.NeedsYou;

/// <summary>
/// The paused run asked to go on once KIFF took the answer to its held call, as the iPad's <c>continueHeld</c> (kiff/tafs#102,
/// #206): tafs cloud's <c>POST /v1/tafs/{taf}/holds/{hold}/continue</c>, as the session. A refusal is never swallowed: a run
/// that had already ended says so in tafs cloud's words (<c>run_ended</c>, kiff/tafs#1812), and an unreached or other refusal
/// says what didn't go through and why. A run KIFF's alert already continued (409, <c>not_paused</c>) went on, so nothing is
/// said. KIFF's alert and the hold's expiry still continue a run this misses.
/// </summary>
public static class HeldContinue
{
    /// <summary>tafs cloud's continue for the taf's hold, by tafs cloud's taf id and KIFF's hold id.</summary>
    public static string Path(string taf, string hold) => $"/v1/tafs/{CloudRead.PathPart(taf)}/holds/{CloudRead.PathPart(hold)}/continue";

    /// <summary>
    /// What Needs you says after it: nothing once tafs cloud took it, or when the run already went on (409, KIFF's alert
    /// continued it first); tafs cloud's own words when it sends them (the run had ended, in the owner's language); otherwise
    /// "{summary} didn't go through." with why.
    /// </summary>
    public static string? Said(WordBook w, string summary, CloudRead r)
    {
        if (r.Answer is not null || r.Status == 409) return null;
        if (r.Words is { } words && !string.IsNullOrWhiteSpace(words)) return words.Trim();
        return w.Get("common.didnt_go_through", ("what", summary), ("why", r.Why ?? w.Get("kiff.kiff_did_not_answer_expected")));
    }
}
