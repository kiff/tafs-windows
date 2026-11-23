using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.NeedsYou;

/// <summary>
/// A Card unit's requests (KIFF's <c>card_unit</c> exceptions, as tafs cloud words them), as the iPad's
/// <c>CardUnitExceptionPanel</c>, <c>discussCardUnit</c> and <c>dismissCardUnit</c>: listed on Needs you from Home and on the
/// taf's Setup; its Coach button for whoever manages the taf and may use Coach; Dismiss, signed by this PC's device key as a
/// refusal is, after the request is read again from KIFF.
/// </summary>
public static class CardUnits
{
    /// <summary>Home's <c>card_unit_exceptions</c>; none, null or not a list is none. One without its id, title or words is left out.</summary>
    public static IReadOnlyList<CardUnitException> Read(JsonElement home) =>
        home.ValueKind == JsonValueKind.Object && home.TryGetProperty("card_unit_exceptions", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).Select(TafSetup.Exception).OfType<CardUnitException>().ToList()
            : [];

    /// <summary>KIFF's one request: <c>GET /v1/me/card-exceptions/{id}</c>, read again before it's dismissed.</summary>
    public static string Path(string id) => "/v1/me/card-exceptions/" + CloudRead.PathPart(id);

    /// <summary>
    /// Whether KIFF's request is still the one shown, as the iPad checks before signing: a Card unit's, still held, for this
    /// taf's agent, with the version the answer signs. Anything else changed meanwhile, and nothing is signed.
    /// </summary>
    public static bool StillHeld(KiffHold current, string agent) =>
        current.Kind == "card_unit" && current.Status == "held" && current.Subject == agent && current.AccountingVersion is not null;

    /// <summary>How a dismissal went: dismissed; not, with why; or dropped by a sign-out.</summary>
    public sealed record Dismissed(bool Done, string? Why = null, bool Stopped = false);

    /// <summary>
    /// Dismiss, as the iPad's <c>dismissCardUnit</c>: KIFF's request read again (<see cref="Path"/>) and checked
    /// (<see cref="StillHeld"/>); Windows Hello over "Dismiss {title}"; then KIFF's <c>kiff.answer.v1</c> refusing it, at the
    /// version read, signed by this PC's device key, and posted to <c>…/reject</c>. Changed meanwhile, it says to read it again;
    /// closed or unconfirmed, nothing is sent; refused, KIFF's words.
    /// </summary>
    public static async Task<Dismissed> DismissAsync(WordBook w, CardUnitException ask, string agent, string tenant,
        Func<string, Task<CloudRead>> read, Func<string, Task<StepConfirmed>> confirm, Func<byte[], Task<string>> deviceSign,
        Func<string, byte[], Task<(int Status, string Body)>> post, Func<string> signedAt, Func<bool> current)
    {
        var answer = await read(Path(ask.Id));
        if (!current()) return new(false, Stopped: true);
        if (answer.Answer is not { } json)
            return new(false, w.Get("needs_you.card_unit.not_dismissed", ("why", answer.Why ?? w.Get("kiff.kiff_did_not_answer_expected"))));
        if (KiffHeld.Hold(json) is not { } held || !StillHeld(held, agent)) return new(false, w.Get("needs_you.card_unit.changed"));
        var confirmed = await confirm(w.Get("needs_you.card_unit.dismiss_prompt", ("title", ask.Title)));
        if (!current()) return new(false, Stopped: true);
        if (confirmed != StepConfirmed.Yes) return new(false, w.Get("needs_you.card_unit.not_dismissed_unconfirmed"));
        // signed_at once the person confirmed: a slow confirmation never ages the answer KIFF checks for skew.
        var at = signedAt();
        var refuse = new HeldAnswer.Refuse();
        var message = HeldAnswers.Message(tenant, held.Id, refuse, held.AccountingVersion!.Value, at);
        string signature;
        try { signature = await deviceSign(message); }
        catch (Exception e) when (e is OperationCanceledException or System.Security.Cryptography.CryptographicException or InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            return current() ? new(false, w.Get("needs_you.card_unit.not_dismissed_unconfirmed")) : new(false, Stopped: true);
        }
        if (!current()) return new(false, Stopped: true);
        var (status, body) = await post(HeldAnswers.Path(held.Id, refuse), HeldAnswers.Body(refuse, at, signature));
        if (!current()) return new(false, Stopped: true);
        return status is >= 200 and <= 299 ? new(true)
            : new(false, w.Get("needs_you.card_unit.not_dismissed", ("why", YourSteps.RefusedWords(w, status, body))));
    }

    /// <summary>The requests still to show: those dismissed here leave every list at once, as the iPad's.</summary>
    public static IReadOnlyList<CardUnitException> Shown(IReadOnlyList<CardUnitException> listed, IReadOnlySet<string> dismissed) =>
        listed.Where(a => !dismissed.Contains(a.Id)).ToList();
}
