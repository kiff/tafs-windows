using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>What tafs cloud did removing a taf: each step (schedule, history, memory, key, name, connections), removed or failed.</summary>
public sealed record TafRemovalAnswer(bool Done, IReadOnlyList<string> Removed, IReadOnlyList<string> Failed);

/// <summary>What came of Remove for good: done, and what to tell the person (none when nothing is said).</summary>
public sealed record RemovalResult(bool Done, string? Words);

/// <summary>
/// Remove for good, as the iPad's <c>removeTaf</c>: signed first, behind one Windows Hello (this PC's passkey asserting
/// <c>kiff.request.v1</c> over <c>DELETE /v1/tafs/{id}</c> and its empty body, as <c>KIFF-Passkey-Assertion</c>); then its
/// Cards revoked in KIFF, so it can't act whatever happens next; then tafs cloud stops it, deletes its runs, chats, memory
/// and key, ends its connections and frees its name. Before it, the confirmation says what goes with it in tafs cloud's
/// words (<c>GET /v1/tafs/{id}/removal</c>, which ends nothing).
/// </summary>
public static class TafRemoval
{
    public static string Path(string id) => "/v1/tafs/" + CloudRead.PathPart(id);

    public static string PreviewPath(string id) => Path(id) + "/removal";

    /// <summary>The empty body the signature covers.</summary>
    public static byte[] Body() => [];

    /// <summary>tafs cloud's words for what goes and what stays ("Qonto goes with it. Stripe stays: Chargebacks uses it."); none blank.</summary>
    public static string? PreviewWords(JsonElement answer) => Lenient.Text(answer, "words")?.Trim() is { Length: > 0 } words ? words : null;

    /// <summary>The confirmation's text: what always goes, then tafs cloud's words when it sent some, as the iPad's <c>removeBody</c>.</summary>
    public static string ConfirmText(WordBook w, string? goes) =>
        goes is { Length: > 0 } ? w.Get("taf.remove_body") + "\n\n" + goes : w.Get("taf.remove_body");

    /// <summary><c>{done, removed, failed}</c>; none for another shape.</summary>
    public static TafRemovalAnswer? Read(JsonElement answer) =>
        Lenient.Field(answer, "done") is { ValueKind: JsonValueKind.True or JsonValueKind.False } done
            ? new(done.GetBoolean(), Steps(answer, "removed"), Steps(answer, "failed"))
            : null;

    static List<string> Steps(JsonElement answer, string name) =>
        Lenient.Array(answer, name).Where(s => s.ValueKind == JsonValueKind.String).Select(s => s.GetString()!).ToList();

    /// <summary>A step in the person's words, as the iPad's <c>removalStep</c>; one it doesn't know, as tafs cloud named it.</summary>
    public static string Step(WordBook w, string step) => step switch
    {
        "connections" => w.Get("taf.remove.step_connections"),
        "schedule" => w.Get("taf.remove.step_schedule"),
        "history" => w.Get("taf.remove.step_history"),
        "memory" => w.Get("taf.remove.step_memory"),
        "key" => w.Get("taf.remove.step_key"),
        "name" => w.Get("taf.remove.step_name"),
        _ => step,
    };

    /// <summary>
    /// Remove for good, in the iPad's order. Windows Hello closed, or any failure to sign: "Not removed. Nothing changed.",
    /// nothing sent. Each Card KIFF doesn't revoke is counted and said. tafs cloud not answering: its Cards' line, then why,
    /// and that it finishes on the next refresh if it began. Not finished: which steps are left. Done: everything that went,
    /// with the name freed. A sign-out at any point ends it quietly.
    /// </summary>
    public static async Task<RemovalResult> RemoveAsync(WordBook w, string id, string name, IReadOnlyList<string> cards, bool cardsRevoked,
        Func<string, Task<(string Header, string Value)>> sign,
        Func<string, Task<bool>> revoke,
        Func<string, (string Header, string Value), Task<CloudRead>> delete,
        Func<bool> current)
    {
        var path = Path(id);
        (string Header, string Value) signature;
        try { signature = await sign(path); }
        catch (Exception e) when (e is OperationCanceledException or PasskeyRefusedException or InvalidDataException or COMException
            or CryptographicException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new(false, current() ? w.Get("taf.remove.not_confirmed") : null);
        }
        if (!current()) return new(false, null);

        var left = 0;
        if (!cardsRevoked)
            foreach (var card in cards.Distinct())
            {
                bool revoked;
                try { revoked = await revoke(RevokePath(card)); }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { revoked = false; }
                if (!current()) return new(false, null);
                if (!revoked) left++;
            }
        var said = left == 0
            ? w.Get("taf.remove.cards_revoked")
            : w.Get("card.kiff_did_not_revoke_cards", ("cards_left", left.ToString(CultureInfo.GetCultureInfo(w.Language))));

        var answer = await delete(path, signature);
        if (!current()) return new(false, null);
        if (answer.Answer is not { } json || Read(json) is not { } removal)
            return new(false, w.Get("taf.remove.cloud_did_not_answer", ("cards", said), ("why", answer.Why ?? w.Get("error.ours"))));
        // What tafs cloud did with its connections: ended, said once; or named among the steps left.
        if (removal.Removed.Contains("connections")) said += w.Get("connect.connections_disconnected");
        if (!removal.Done)
            return new(false, w.Get("taf.remove.not_finished_after", ("cards", said),
                ("failed", string.Join(", ", removal.Failed.Select(s => Step(w, s))))));
        return new(true, w.Get("connect.schedule_chats_run_reports_memory", ("cards", said), ("taf", name)));
    }

    /// <summary>KIFF's revoke of one Card, a session's POST with no signature (the iPad's <c>KIFFClient.revoke</c>).</summary>
    public static string RevokePath(string card) => "/v1/me/mandates/" + CloudRead.PathPart(card) + "/revoke";

    public static byte[] RevokeBody() => "{}"u8.ToArray();
}
