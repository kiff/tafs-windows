using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.NeedsYou;

/// <summary>
/// The paused run asked to go on once KIFF took the answer to its held call, as the iPad's <c>continueHeld</c> (kiff/tafs#102,
/// #206): tafs cloud's <c>POST /v1/tafs/{taf}/holds/{hold}/continue</c>, as the session. Unlike the phones, a refusal is never
/// swallowed (the PM's 04:46): a run that had already ended says so in tafs cloud's words (<c>run_ended</c>, kiff/tafs#1812),
/// and any other refusal says what didn't go through and why. KIFF's alert and the hold's expiry still continue the run.
/// </summary>
public static class HeldContinue
{
    /// <summary>tafs cloud's continue for the taf's hold, by tafs cloud's taf id and KIFF's hold id.</summary>
    public static string Path(string taf, string hold) => $"/v1/tafs/{CloudRead.PathPart(taf)}/holds/{CloudRead.PathPart(hold)}/continue";

    /// <summary>
    /// What Needs you says after it: nothing once tafs cloud took it; tafs cloud's own words when it sends them (the run had
    /// ended, in the owner's language); otherwise "{summary} didn't go through." with why.
    /// </summary>
    public static string? Said(WordBook w, string summary, CloudRead r)
    {
        if (r.Answer is not null) return null;
        if (r.Words is { } words && !string.IsNullOrWhiteSpace(words)) return words.Trim();
        return w.Get("common.didnt_go_through", ("what", summary), ("why", r.Why ?? w.Get("kiff.kiff_did_not_answer_expected")));
    }
}
