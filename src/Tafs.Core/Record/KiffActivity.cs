using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tafs.Core.Coach;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Record;

/// <summary>How a call ended at the tool, as KIFF's activity says: done, refused_by_tool, not_sent, sending, unknown or not_recorded.</summary>
public sealed record ToolOutcome(string Status, string? Message = null, string? RefusedBy = null, string? Code = null);

/// <summary>The money a call moves, as KIFF writes it: a decimal string and its unit ("150.00", "EUR"); a gateway call's has no unit.</summary>
public sealed record CallAmount(string Value, string? Unit = null);

/// <summary>The answer to a held call: approved or refused, by KIFF's role for whoever answered, or the person reading.</summary>
public sealed record CallAnswer(string Decision, string? By = null, bool? You = null, bool? Signed = null, string? Device = null, DateTimeOffset? At = null);

/// <summary>A call's KIFF receipt: signed, or pending until KIFF's next sweep.</summary>
public sealed record CallReceipt(string TraceId, string Status);

/// <summary>
/// One call of the account's, as KIFF's <c>GET /v1/me/activity</c> records it (the iPad's <c>KIFFClient.ActivityCall</c>):
/// what a taf did, newest first, with the tool's title for people.
/// </summary>
public sealed record ActivityCall(string ProposalId, string ActionName, string Outcome, DateTimeOffset CreatedAt, bool Held,
    string? Title = null, string? ActorId = null, ToolOutcome? Tool = null, CallAmount? Amount = null, bool? ReadOnly = null,
    string? EntityId = null, string? OnHash = null, string? OnArg = null, CallAnswer? Answer = null, CallReceipt? Receipt = null,
    IReadOnlyList<string>? Reasons = null, DateTimeOffset? MadeAt = null, string? Reason = null, string? Detail = null);

/// <summary>The Record from KIFF's activity, as the iPad's <c>KIFFClient.activity</c> and <c>KIFFMapping.record</c>.</summary>
public static class KiffActivity
{
    /// <summary>The account's latest calls, as the iPad reads them for its Record.</summary>
    public const string Path = "/v1/me/activity?limit=100";

    /// <summary>
    /// KIFF's answer, read as strictly as the iPad's decoder: <c>calls</c> is there, and each call has every field it requires
    /// in its shape; any other is absent, null or of its shape. One call it can't read fails the whole read, as the iPad's
    /// <c>try?</c> does, and the Record stays as it was.
    /// </summary>
    public static IReadOnlyList<ActivityCall>? Read(JsonElement answer)
    {
        if (!Shape.Required(answer, "calls", v => v.ValueKind == JsonValueKind.Array)) return null;
        var calls = new List<ActivityCall>();
        foreach (var c in answer.GetProperty("calls").EnumerateArray())
        {
            if (Call(c) is not { } call) return null;
            calls.Add(call);
        }
        return calls;
    }

    static ActivityCall? Call(JsonElement c)
    {
        string[] required = ["proposal_id", "action_name", "outcome"];
        string[] optional = ["title", "actor_id", "entity_id", "on_hash", "on_arg", "reason", "detail"];
        if (!required.All(n => Shape.Required(c, n, Shape.Text)) || !optional.All(n => Shape.Optional(c, n, Shape.Text))
            || !Shape.Required(c, "created_at", IsAt) || !Shape.Required(c, "held", Shape.Bool)
            || !Shape.Optional(c, "read_only", Shape.Bool) || !Shape.Optional(c, "made_at", IsAt)
            || !Shape.Optional(c, "reasons", v => Shape.List(v, Shape.Text))
            || !Shape.Optional(c, "tool_outcome", IsTool) || !Shape.Optional(c, "amount", IsAmount)
            || !Shape.Optional(c, "answer", IsAnswer) || !Shape.Optional(c, "receipt", IsReceipt)) return null;
        string? T(JsonElement o, string n) => Lenient.Text(o, n);
        JsonElement? F(string n) => Lenient.Field(c, n) is { ValueKind: JsonValueKind.Object } o ? o : null;
        return new ActivityCall(T(c, "proposal_id")!, T(c, "action_name")!, T(c, "outcome")!, At(c, "created_at")!.Value, Lenient.Bool(c, "held"),
            T(c, "title"), T(c, "actor_id"),
            F("tool_outcome") is { } o ? new ToolOutcome(T(o, "status")!, T(o, "message"), T(o, "refused_by"), T(o, "code")) : null,
            F("amount") is { } a ? new CallAmount(T(a, "value")!, T(a, "unit")) : null,
            Lenient.Field(c, "read_only") is { ValueKind: JsonValueKind.True or JsonValueKind.False } r ? r.GetBoolean() : null,
            T(c, "entity_id"), T(c, "on_hash"), T(c, "on_arg"),
            F("answer") is { } x ? new CallAnswer(T(x, "decision")!, T(x, "by"), Flag(x, "you"), Flag(x, "signed"), T(x, "device"), At(x, "at")) : null,
            F("receipt") is { } p ? new CallReceipt(T(p, "trace_id")!, T(p, "status")!) : null,
            Lenient.Field(c, "reasons") is { ValueKind: JsonValueKind.Array } why ? why.EnumerateArray().Select(e => e.GetString()!).ToList() : null,
            At(c, "made_at"), T(c, "reason"), T(c, "detail"));
    }

    static bool IsTool(JsonElement o) => Shape.Required(o, "status", Shape.Text)
        && new[] { "message", "refused_by", "code" }.All(n => Shape.Optional(o, n, Shape.Text));

    static bool IsAmount(JsonElement o) => Shape.Required(o, "value", Shape.Text) && Shape.Optional(o, "unit", Shape.Text);

    static bool IsAnswer(JsonElement o) => Shape.Required(o, "decision", Shape.Text) && Shape.Optional(o, "by", Shape.Text)
        && Shape.Optional(o, "you", Shape.Bool) && Shape.Optional(o, "signed", Shape.Bool) && Shape.Optional(o, "device", Shape.Text)
        && Shape.Optional(o, "at", IsAt);

    static bool IsReceipt(JsonElement o) => Shape.Required(o, "trace_id", Shape.Text) && Shape.Required(o, "status", Shape.Text);

    static bool? Flag(JsonElement o, string n) => Lenient.Field(o, n) is { ValueKind: JsonValueKind.True or JsonValueKind.False } v ? v.GetBoolean() : null;

    /// <summary>A time as the iPad's KIFF decoder takes it: ISO 8601 with its offset or Z, with or without fractions of a second.</summary>
    static bool IsAt(JsonElement v) => v.ValueKind == JsonValueKind.String && Parse(v.GetString()!) is not null;

    static DateTimeOffset? At(JsonElement o, string n) => Lenient.Text(o, n) is { } s ? Parse(s) : null;

    static DateTimeOffset? Parse(string s) =>
        Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:?\d{2})$")
        && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : null;

    /// <summary>
    /// The Record's lines, as the iPad's <c>KIFFMapping.record</c>: one a call of a taf on Home's list (by its agent in
    /// KIFF), newest first as KIFF sends them. KIFF's decision and what the tool did are kept apart: a call is done only
    /// when the tool said so. Its title has its amount ("Refund a payment · €150.00"); its detail says who decided and
    /// what became of it once there's an answer or a receipt, else what the tool did.
    /// </summary>
    public static IReadOnlyList<RecordEntry> Lines(WordBook w, IEnumerable<ActivityCall> calls, Func<string, (string Name, int Colour)?> tafOf)
    {
        var lines = new List<RecordEntry>();
        foreach (var c in calls)
        {
            if (c.ActorId is null || tafOf(c.ActorId) is not { } taf) continue;
            var allowed = c.Outcome == "allowed";
            var (kind, detail) = c.Outcome switch
            {
                "allowed" => Executed(w, c.Tool, c.Held, Counted(c)),
                "owner_required" or "approval_required" => (EntryKind.Held, w.Get("record.waiting_for_you")),
                // KIFF's own words for why, when it has them.
                _ => (EntryKind.Refused, c.Reason is { } why ? w.Get("record.not_sent_because", ("reason", why)) : w.Get("record.not_sent")),
            };
            if (allowed && c.Tool is { Status: "refused_by_tool", RefusedBy: "connection", Code: not null })
                detail = c.Detail ?? w.Get("record.connection_refused_before_sending");
            if (c.Receipt is not null || c.Answer is not null)
                detail = Decided(w, c.Answer, kind, allowed && c.Held) + " · " + Result(w, kind);
            var name = Name(c.ActionName, c.Title);
            var amount = AmountText(w, c.Amount);
            lines.Add(new RecordEntry(taf.Name, taf.Colour, kind, amount is null ? name : $"{name} · {amount}", detail, c.CreatedAt, amount, c));
        }
        return lines;
    }

    /// <summary>
    /// What the person reads for an action, as the iPad's <c>KIFFMapping.name</c>: its tool's title as KIFF gives it, else the
    /// action in words. Never a name made up here.
    /// </summary>
    public static string Name(string action, string? title) => title?.Trim() is { Length: > 0 } t ? t : CoachChat.ToolWords(action);

    /// <summary>What a write's amount counts on the Card's totals, as the iPad's <c>counted</c>: money, some other unit, or nothing.</summary>
    public enum Counts { Money, Amount, Nothing }

    public static Counts Counted(ActivityCall c) =>
        c.Amount is not { } a || c.ReadOnly == true ? Counts.Nothing : IsCurrency(a.Unit) ? Counts.Money : Counts.Amount;

    /// <summary>An ISO 4217 code, as the iPad's <c>isCurrency</c>: a gateway's unit can name anything, so only a currency is money.</summary>
    static bool IsCurrency(string? unit) => unit is { Length: 3 } && Currencies.Contains(unit.ToUpperInvariant());

    static readonly HashSet<string> Currencies = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(c => { try { return new RegionInfo(c.Name).ISOCurrencySymbol; } catch (ArgumentException) { return ""; } })
        .Where(s => s.Length == 3).ToHashSet();

    /// <summary>
    /// What the tool did with an allowed call, as the iPad's <c>executed</c>: its kind and the words for it. A call the tool
    /// refused still counts against the Card's totals, and says so. Not recorded, none, or a status this app doesn't know
    /// yet is unknown, never done.
    /// </summary>
    public static (EntryKind Kind, string Detail) Executed(WordBook w, ToolOutcome? o, bool approved, Counts counts = Counts.Nothing)
    {
        var why = o?.Message is { Length: > 0 } m ? ": " + m : "";
        var allowed = w.Get(approved ? "record.approved_by_you" : "record.allowed_by_card");
        var who = w.Get("record.the_tool");
        return o?.Status switch
        {
            "done" => approved ? (EntryKind.Approved, w.Get("record.approved_then_done")) : (EntryKind.Done, w.Get("record.done_within_card")),
            "refused_by_tool" => (EntryKind.NotDone, counts switch
            {
                Counts.Money => w.Get("record.tool_refused_money", ("who", who)),
                Counts.Amount => w.Get("record.tool_refused_amount", ("who", who)),
                _ => w.Get("record.tool_refused_counts_why", ("who", who), ("why", why)),
            }),
            "not_sent" => (EntryKind.NotDone, w.Get("record.not_sent_nothing_reached_why", ("allowed", allowed), ("why", why))),
            "sending" => (EntryKind.Sending, w.Get("record.sent_waiting", ("allowed", allowed))),
            "unknown" => (EntryKind.Unknown, w.Get("record.answer_lost", ("allowed", allowed))),
            _ => (EntryKind.Unknown, w.Get("record.not_recorded", ("allowed", allowed))),
        };
    }

    /// <summary>
    /// "€150.00", as the iPad's <c>amountText</c>: as the app writes money everywhere when its unit is a currency; the value
    /// and its unit as written when it isn't; the value alone with none. None when the value isn't a decimal, or can't be
    /// written as the money it is (more than 18 decimals, or more minor units than a whole number holds).
    /// </summary>
    public static string? AmountText(WordBook w, CallAmount? amount)
    {
        if (amount is null || !decimal.TryParse(amount.Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            return null;
        if (string.IsNullOrEmpty(amount.Unit)) return amount.Value;
        var unit = amount.Unit;
        if (unit.Length != 3 || unit != unit.ToUpperInvariant() || !Currencies.Contains(unit)) return $"{amount.Value} {unit}";
        var scale = amount.Value.Split('.') is { Length: > 1 } parts ? parts[1].Length : 0;
        // Past what a figure can be written with, or past a whole number of minor units: no amount rather than a wrong one,
        // and never a crash on KIFF's answer.
        if (scale > KiffAmount.MaxScale) return null;
        try { return new KiffAmount(checked((long)(value * Pow10(scale))), unit, scale).Figure(w); }
        catch (OverflowException) { return null; }
    }

    static decimal Pow10(int n)
    {
        var p = 1m;
        for (var i = 0; i < n; i++) p *= 10;
        return p;
    }

    /// <summary>Who decided, as the iPad's <c>decidedLine</c>: whoever answered it, else its Card or KIFF's refusal.</summary>
    static string Decided(WordBook w, CallAnswer? answer, EntryKind kind, bool ownerApproved)
    {
        if (answer is not null)
            return w.Get(answer.Decision == "refused" ? "record.decided.refused_by" : "record.decided.approved_by", ("who", Answerer(w, answer)));
        return kind switch
        {
            EntryKind.Refused => w.Get("record.decided.refused_by_card"),
            EntryKind.Held => w.Get("record.decided.waiting_for_you"),
            EntryKind.Revoked => w.Get("card.card_revoked"),
            EntryKind.Started => w.Get("record.decided.card_issued"),
            _ => w.Get(ownerApproved ? "record.decided.approved_by_you" : "record.decided.within_card"),
        };
    }

    /// <summary>Who answered, as the iPad's <c>answerer</c>: "you", or KIFF's role for them ("an admin").</summary>
    internal static string Answerer(WordBook w, CallAnswer a)
    {
        if (a.You == true) return w.Get("record.answerer.you");
        if (string.IsNullOrEmpty(a.By)) return w.Get("record.answerer.someone_else");
        return a.By is "admin" or "approver" or "editor" or "member" or "manager" or "owner" or "viewer"
            ? w.Get("people.role_phrase." + a.By) : w.Get("people.role_phrase.other", ("role", a.By.Replace('_', ' ')));
    }

    /// <summary>What became of it at the tool, as the iPad's <c>resultLine</c>.</summary>
    static string Result(WordBook w, EntryKind kind) => w.Get(kind switch
    {
        EntryKind.Done or EntryKind.Approved => "record.result.done",
        EntryKind.Refused or EntryKind.Revoked => "record.nothing_sent",
        EntryKind.NotDone => "record.result.not_done",
        EntryKind.Held => "record.result.nothing_sent_yet",
        EntryKind.Sending => "record.result.no_answer_yet",
        EntryKind.Started => "record.result.started",
        _ => "record.result.unknown",
    });
}
