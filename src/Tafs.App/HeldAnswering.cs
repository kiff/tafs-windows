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
sealed record HeldAnswering(bool MayAnswerHolds, Func<HeldCall, bool> CanAnswer, bool Approves, bool ManagesCards,
    IReadOnlyDictionary<string, string> Lasting, IReadOnlySet<string> Sending)
{
    /// <summary>Signed out, or the sample: nothing is sent. The sample's calls show the answers, as the iPad's, with the gate for approving.</summary>
    public static readonly HeldAnswering None = new(true, call => call.KiffId is null, false, false, new Dictionary<string, string>(), new HashSet<string>());
}
