using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Tafs.Core.Approvals;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Home;

/// <summary>What came of Discard: done, or why not (none when nothing is said), and the Cards tafs cloud still found in force.</summary>
public sealed record DiscardResult(bool Done, string? Why, IReadOnlyList<string>? CardsLeft = null);

/// <summary>
/// Discard, for a taf being set up, as the iPad's <c>discardSettingUp</c> (kiff/tafs <c>docs/api.md</c>,
/// <c>POST /v1/home/setting-up/{kiff_agent}/discard</c>): signed first, as a removal is, with this PC's passkey behind one
/// Windows Hello (<c>KIFF-Passkey-Assertion</c> over <c>kiff.request.v1</c>: POST, the path and the empty body); then its
/// Cards revoked in KIFF (<c>discard.cards</c> on Home, and any a refused Discard named); then tafs cloud is asked, which
/// takes it only once KIFF holds none of its Cards in force.
/// </summary>
public static class SetupDiscard
{
    public static string Path(string agent) => "/v1/home/setting-up/" + CloudRead.PathPart(agent) + "/discard";

    /// <summary>KIFF's revoke of one Card (the iPad's <c>KIFFClient.revoke</c>): a session's POST, not signed.</summary>
    public static string RevokePath(string card) => "/v1/me/mandates/" + CloudRead.PathPart(card) + "/revoke";

    public static byte[] RevokeBody() => "{}"u8.ToArray();

    /// <summary>The empty body the signature covers: nothing is sent with it.</summary>
    public static byte[] Body() => [];

    /// <summary>tafs cloud's refusal while KIFF still holds some of its Cards in force, with their ids.</summary>
    public const string CardsInForce = "cards_in_force";

    /// <summary>
    /// Discard, in the iPad's order. Windows Hello closed, or any failure to sign, sends nothing: "Not removed. Nothing
    /// changed." A Card KIFF doesn't revoke is counted, and tafs cloud is asked anyway: it refuses while one is in force
    /// (<see cref="CardsInForce"/>), naming them, so the next Discard revokes those too; it never fails the same way twice.
    /// Any other refusal says tafs cloud's words. A sign-out at any point ends it quietly.
    /// </summary>
    public static async Task<DiscardResult> DiscardAsync(WordBook w, SettingUp taf, IReadOnlyList<string> left,
        Func<string, Task<(string Header, string Value)>> sign,
        Func<string, Task<bool>> revoke,
        Func<string, (string Header, string Value), Task<CloudRead>> post,
        Func<bool> current)
    {
        if (taf.Agent is not { } agent || taf.Discard is null) return new(false, null);
        var path = Path(agent);
        (string Header, string Value) signature;
        try { signature = await sign(path); }
        catch (Exception e) when (e is OperationCanceledException or PasskeyRefusedException or InvalidDataException or COMException
            or CryptographicException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new(false, current() ? w.Get("taf.remove.not_confirmed") : null);
        }
        if (!current()) return new(false, null);

        var failed = 0;
        foreach (var card in taf.Cards.Concat(left).Distinct())
        {
            bool revoked;
            try { revoked = await revoke(RevokePath(card)); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { revoked = false; }
            if (!current()) return new(false, null);
            if (!revoked) failed++;
        }

        var answer = await post(path, signature);
        if (!current()) return new(false, null);
        if (answer.Answer is not null) return new(true, null);
        if (answer.Code == CardsInForce)
        {
            var count = Math.Max(Math.Max(answer.Cards.Count, failed), 1);
            return new(false, w.Get("card.kiff_did_not_revoke_cards", ("cards_left", count.ToString(CultureInfo.GetCultureInfo(w.Language)))),
                answer.Cards.Count > 0 ? answer.Cards : null);
        }
        return new(false, answer.Why ?? w.Get("error.ours"));
    }
}
