using System.Globalization;
using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Record;

/// <summary>
/// One step KIFF signed, as the iPad's receipt sheet reads it: its words (KIFF's message, else its kind), when, its data's
/// hash shortened, its kind, and what it signed of the call (what it acted on, its amount, a hold's answer).
/// </summary>
public sealed record ReceiptStep(string Words, DateTimeOffset? At, string Hash, string Kind, string? OnHash = null,
    CallAmount? Amount = null, string? Answer = null);

/// <summary>
/// A line's KIFF receipt, as the iPad's <c>ReceiptView</c>: <c>GET /v1/receipts/{trace_id}</c>, kept as KIFF wrote it, its
/// signed steps, and the sheet's facts about the line.
/// </summary>
public static class Receipt
{
    public static string Path(string traceId) => "/v1/receipts/" + CloudRead.PathPart(traceId);

    /// <summary>
    /// "What happened" is what KIFF signed for this line, as the iPad's <c>showsWhatHappened</c>: drawn only once this PC
    /// checked KIFF's signature and the receipt matches the line. Not signed yet, not checking out, or still checking, its
    /// steps aren't drawn as what happened.
    /// </summary>
    public static bool ShowsWhatHappened(ReceiptCheck? check, bool mismatch) => check is ReceiptCheck.Verified && !mismatch;

    /// <summary>
    /// The receipt's steps as KIFF signed them (its <c>causal_chain</c>, taken as written): what the sheet shows as happened.
    /// None when the receipt or its chain can't be read.
    /// </summary>
    public static IReadOnlyList<ReceiptStep> Steps(byte[] raw)
    {
        if (RawJson.Value(raw, "causal_chain") is not { } chain) return [];
        try
        {
            using var doc = JsonDocument.Parse(chain);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.EnumerateArray().Any(s => s.ValueKind != JsonValueKind.Object)) return [];
            return doc.RootElement.EnumerateArray().Select(s =>
            {
                var kind = Lenient.Text(s, "kind") ?? "";
                var hash = Lenient.Text(s, "data_hash") ?? "";
                var data = Lenient.Field(s, "data") is { ValueKind: JsonValueKind.Object } d ? d : (JsonElement?)null;
                var amount = data is { } o && Lenient.Field(o, "amount") is { ValueKind: JsonValueKind.Object } a && Lenient.Text(a, "value") is { } value
                    ? new CallAmount(value, Lenient.Text(a, "unit")) : null;
                return new ReceiptStep(Lenient.Text(s, "message") is { Length: > 0 } m ? m : kind.Replace('_', ' '), At(s, "occurred_at"),
                    hash.Length > 8 ? $"{hash[..4]}…{hash[^4..]}" : hash, kind,
                    data is { } on ? Lenient.Text(on, "on_hash") : null, amount, data is { } x ? Lenient.Text(x, "answer") : null);
            }).ToList();
        }
        catch (JsonException) { return []; }
    }

    /// <summary>A top-level word of the receipt (<c>issued_at</c>), or of its attestation (<c>signature</c>).</summary>
    public static string? Field(byte[] raw, string key, bool attestation = false)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var o = doc.RootElement;
            if (attestation) o = Lenient.Field(o, "attestation") is { ValueKind: JsonValueKind.Object } a ? a : default;
            return Lenient.Text(o, key);
        }
        catch (JsonException) { return null; }
    }

    static DateTimeOffset? At(JsonElement o, string name) =>
        DateTimeOffset.TryParse(Lenient.Text(o, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null;

    /// <summary>"Nov 15, 2026, 09:40:12", in this PC's zone, as the sheet's times are written; a step's time alone without its date.</summary>
    public static string Shown(WordBook w, DateTimeOffset at, TimeZoneInfo zone, bool withDate = true)
    {
        var culture = Tafs.Core.Words.Locale.Culture(w.Language);
        var local = TimeZoneInfo.ConvertTime(at, zone);
        return withDate ? local.ToString("d MMM yyyy, HH:mm:ss", culture) : local.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>The sheet's head, as the iPad's <c>stateLabel</c>: who answered it, else what became of it.</summary>
    public static string StateLabel(WordBook w, RecordEntry e)
    {
        if (e.Call?.Answer is { } answer)
            return w.Get(answer.Decision == "refused" ? "receipt.refused_by" : "receipt.approved_once_by", ("who", KiffActivity.Answerer(w, answer)));
        return e.Kind switch
        {
            EntryKind.Done => w.Get("receipt.label.done"),
            EntryKind.Approved => w.Get("receipt.label.approved"),
            EntryKind.Refused => w.Get("record.filter.refused"),
            EntryKind.Held => w.Get("receipt.label.held"),
            EntryKind.NotDone => w.Get("receipt.label.not_done"),
            EntryKind.Sending => w.Get("receipt.label.sending"),
            EntryKind.Revoked => w.Get("card.card_revoked"),
            EntryKind.Started => w.Get("receipt.label.started"),
            _ => w.Get("receipt.label.unknown"),
        };
    }

    /// <summary>The Outcome fact, as the iPad's <c>outcome</c>: done (with the money moved), not sent and by whom, waiting, else the line's detail.</summary>
    public static string Outcome(WordBook w, RecordEntry e) => e.Kind switch
    {
        EntryKind.Done or EntryKind.Approved => e.Amount is { } amount ? w.Get("receipt.outcome.moved", ("amount", amount)) : w.Get("receipt.outcome.done"),
        EntryKind.Refused => e.Call?.Answer is { Decision: "refused" } a
            ? w.Get("receipt.outcome.refused_by", ("who", KiffActivity.Answerer(w, a))) : w.Get("receipt.outcome.card_refused"),
        EntryKind.Held => w.Get("receipt.outcome.waiting"),
        _ => e.Detail,
    };

    /// <summary>Who decided, in the receipt's own words, as the iPad's <c>decisionWords</c>: "held → approved once by you".</summary>
    public static string DecisionWords(WordBook w, RecordEntry e)
    {
        if (e.Call?.Answer is { } answer)
            return w.Get(answer.Decision == "refused" ? "receipt.decision.held_refused_by" : "receipt.decision.held_approved_once_by",
                ("who", KiffActivity.Answerer(w, answer)));
        return e.Kind switch
        {
            EntryKind.Refused => w.Get("receipt.decision.refused_by_card"),
            // Still waiting: the Card held it, it didn't allow it.
            EntryKind.Held => w.Get("receipt.decision.held_by_card"),
            _ => w.Get(OwnerApproved(e) ? "receipt.decision.held_approved_by_you" : "receipt.decision.allowed_by_card"),
        };
    }

    /// <summary>
    /// The Decided by fact, as the iPad's <c>decidedBy</c>: whoever answered (signed on which device, when it was), you
    /// approving once, revoking or issuing, else KIFF against the Card for KIFF's lines, or the Card.
    /// </summary>
    public static string DecidedBy(WordBook w, RecordEntry e)
    {
        if (e.Call?.Answer is { } answer)
        {
            var who = KiffActivity.Answerer(w, answer);
            var how = answer.You == true ? w.Get("receipt.decided_by.you") : who[..1].ToUpper(Tafs.Core.Words.Locale.Culture(w.Language)) + who[1..];
            return answer.Signed == true
                ? w.Get("receipt.decided_by.signed_on", ("who", how), ("device", answer.Device ?? w.Get("receipt.decided_by.your_device"))) : how;
        }
        if (e.Kind == EntryKind.Approved || OwnerApproved(e)) return w.Get("receipt.decided_by.you_approving_once");
        return w.Get(e.Kind switch
        {
            EntryKind.Revoked => "receipt.decided_by.you_revoking",
            EntryKind.Started => "receipt.decided_by.you_issuing",
            _ => e.Call is not null ? "receipt.decided_by.kiff" : "receipt.decided_by.card",
        });
    }

    /// <summary>Allowed after the person approved it, held first.</summary>
    static bool OwnerApproved(RecordEntry e) => e.Call is { Outcome: "allowed", Held: true };

    /// <summary>
    /// Where the line and KIFF's signed steps disagree, as the iPad's <c>onMismatch</c>, <c>amountMismatch</c> and <c>answerMismatch</c>, in that
    /// order: a named line's payment isn't the value KIFF hashed (<c>on_hash</c>), the
    /// decision step signed another amount, or the hold's settling another answer (KIFF records a refusal as "reject").
    /// None when they agree, or the steps don't say. Amounts are compared as numbers with their unit, so "150.00" and "150"
    /// agree (the iPad compares their strings); one that isn't a number is compared as written.
    /// </summary>
    public static string? Mismatch(WordBook w, RecordEntry e, IReadOnlyList<ReceiptStep> steps)
    {
        // The payment first, as the iPad's order: a named line (what it acted on, kiff/tafs#167) is held to what KIFF signed.
        if (e.OnValue is { } on && steps.Select(s => s.OnHash).FirstOrDefault(h => h is not null) is { } signedOn && !RecordNaming.IsOn(on, signedOn))
            return w.Get("receipt.check.payment_mismatch");
        if (e.Call?.Amount is { } line && steps.Select(s => s.Amount).FirstOrDefault(a => a is not null) is { } signed
            && (!SameValue(line.Value, signed.Value) || (line.Unit ?? "") != (signed.Unit ?? "")))
            return w.Get("receipt.check.amount_mismatch");
        if (e.Call?.Answer is { } answer && steps.FirstOrDefault(s => s.Kind == "hold_settled")?.Answer is { } settled
            && (answer.Decision == "refused") != (settled == "reject"))
            return w.Get("receipt.check.answer_mismatch");
        return null;
    }

    /// <summary>The same amount: equal as decimals when both are numbers, else equal as written.</summary>
    static bool SameValue(string line, string signed)
    {
        const System.Globalization.NumberStyles number = System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint;
        var c = System.Globalization.CultureInfo.InvariantCulture;
        return decimal.TryParse(line, number, c, out var a) && decimal.TryParse(signed, number, c, out var b) ? a == b : line == signed;
    }
}

/// <summary>
/// A top-level value's own bytes in a JSON object, as written, as the iPad's <c>RawJSON</c>: the receipt's signed chain is
/// hashed as KIFF wrote it, never as a decoder would write it again.
/// </summary>
public static class RawJson
{
    public static byte[]? Value(byte[] b, string key)
    {
        var i = Space(b, 0);
        if (i >= b.Length || b[i] != '{') return null;
        i++;
        while (true)
        {
            i = Space(b, i);
            if (i >= b.Length || b[i] == '}' || b[i] != '"' || StringEnd(b, i) is not { } keyEnd) return null;
            string? name;
            try { name = JsonSerializer.Deserialize<string>(b.AsSpan(i, keyEnd - i)); }
            catch (JsonException) { name = null; }
            i = Space(b, keyEnd);
            if (i >= b.Length || b[i] != ':') return null;
            i = Space(b, i + 1);
            if (ValueEnd(b, i) is not { } valueEnd) return null;
            if (name == key) return b[i..valueEnd];
            i = Space(b, valueEnd);
            if (i >= b.Length || b[i] != ',') return null;
            i++;
        }
    }

    /// <summary>An object's own keys, in the order written (not those of objects inside it); none when it isn't an object that reads to its end.</summary>
    public static IReadOnlyList<string>? Keys(byte[] b)
    {
        var i = Space(b, 0);
        if (i >= b.Length || b[i] != '{') return null;
        i = Space(b, i + 1);
        var keys = new List<string>();
        if (i < b.Length && b[i] == '}') return keys;
        while (true)
        {
            if (i >= b.Length || b[i] != '"' || StringEnd(b, i) is not { } keyEnd) return null;
            string? name;
            try { name = JsonSerializer.Deserialize<string>(b.AsSpan(i, keyEnd - i)); }
            catch (JsonException) { return null; }
            if (name is null) return null;
            keys.Add(name);
            i = Space(b, keyEnd);
            if (i >= b.Length || b[i] != ':') return null;
            i = Space(b, i + 1);
            if (ValueEnd(b, i) is not { } valueEnd) return null;
            i = Space(b, valueEnd);
            if (i >= b.Length) return null;
            if (b[i] == '}') return keys;
            if (b[i] != ',') return null;
            i = Space(b, i + 1);
        }
    }

    /// <summary>No key of the object written twice. An object it can't read isn't unique.</summary>
    public static bool KeysAreUnique(byte[] b) => Keys(b) is { } keys && keys.Distinct().Count() == keys.Count;

    static int Space(byte[] b, int i)
    {
        while (i < b.Length && b[i] is 0x20 or 0x09 or 0x0A or 0x0D) i++;
        return i;
    }

    /// <summary>The index after a string's closing quote; <paramref name="start"/> is its opening quote.</summary>
    static int? StringEnd(byte[] b, int start)
    {
        for (var i = start + 1; i < b.Length; i++)
        {
            if (b[i] == '\\') { i++; continue; }
            if (b[i] == '"') return i + 1;
        }
        return null;
    }

    /// <summary>The index after a value: a string, an object or array to its matching close, or a literal or number to the next delimiter.</summary>
    static int? ValueEnd(byte[] b, int start)
    {
        if (start >= b.Length) return null;
        if (b[start] == '"') return StringEnd(b, start);
        if (b[start] is (byte)'{' or (byte)'[')
        {
            var depth = 0;
            for (var i = start; i < b.Length; i++)
            {
                if (b[i] == '"')
                {
                    if (StringEnd(b, i) is not { } end) return null;
                    i = end - 1;
                }
                else if (b[i] is (byte)'{' or (byte)'[') depth++;
                else if (b[i] is (byte)'}' or (byte)']' && --depth == 0) return i + 1;
            }
            return null;
        }
        var j = start;
        while (j < b.Length && b[j] is not ((byte)',' or (byte)'}' or (byte)']' or 0x20 or 0x09 or 0x0A or 0x0D)) j++;
        return j > start ? j : null;
    }
}
