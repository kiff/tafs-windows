using Tafs.Core.NeedsYou;

namespace Tafs.App;

/// <summary>
/// Who's answering the held calls on this PC and how it answers them (the iPad's <c>mayAnswerHolds</c>, <c>canAnswer</c>,
/// <c>canManageCards</c>, <c>unanswerable</c> and the cards being sent), for the card's answers.
/// </summary>
/// <param name="CanAnswer">Whether this PC answers the call: KIFF's hold with its version, the tenant and the device key.</param>
/// <param name="Approves">A passkey session with this PC's own passkey: Approve once and Extra room.</param>
/// <param name="Lasting">KIFF's lasting refusal for a hold, by its id.</param>
/// <param name="Sending">The holds whose answer is on its way.</param>
/// <param name="OffersAllow">Whether the card offers "Allow this from now on" (<see cref="HoldAllow.Offers"/>).</param>
/// <param name="MayImprove">Whether this person may improve the call's taf in Coach (the iPad's <c>mayImproveInCoach</c>):
/// they manage it and may use Coach. "Change the limit in Coach mode" shows only then.</param>
sealed record HeldAnswering(bool MayAnswerHolds, Func<HeldCall, bool> CanAnswer, bool Approves, bool ManagesCards,
    IReadOnlyDictionary<string, string> Lasting, IReadOnlySet<string> Sending, Func<HeldCall, bool> OffersAllow,
    Func<HeldCall, bool>? MayImprove = null)
{
    /// <summary>Signed out, or the sample: nothing is sent. The sample's calls show the answers, as the iPad's, with the gate for approving.</summary>
    public static readonly HeldAnswering None = new(true, call => call.KiffId is null, false, false, new Dictionary<string, string>(), new HashSet<string>(),
        _ => false);
}
