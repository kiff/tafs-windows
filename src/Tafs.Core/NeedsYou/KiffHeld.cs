using System.Globalization;
using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.Coach;
using Tafs.Core.Record;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.NeedsYou;

/// <summary>One breach of a Card a held call names, as the iPad's <c>CardException.Breach</c>.</summary>
public sealed record HoldBreach(string Outcome, string? Reason = null, long? Limit = null, string? Quantity = null, string? Unit = null,
    int? Scale = null, bool? UnitAmbiguous = null, long? Used = null, long? Draw = null, string? Window = null, string? Per = null,
    string? Value = null);

/// <summary>
/// A call KIFF held, as the iPad's <c>KIFFClient.CardException</c>: what it is, the Card's breaches, its parameters and their
/// units, its arguments as text, and the taf's own words, kept as KIFF sends them.
/// </summary>
public sealed record KiffHold(
    string Id, string Subject, string Action, string EntityId, string Status, IReadOnlyList<HoldBreach> Breaches,
    IReadOnlyDictionary<string, long>? Parameters, string? Reason, IReadOnlyDictionary<string, (string Unit, int Scale)>? ParameterUnits,
    IReadOnlyList<string>? NextSteps, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, int? AccountingVersion)
{
    public string? Title { get; init; }
    public IReadOnlyDictionary<string, string>? Arguments { get; init; }
    public string? ProposalId { get; init; }
    public string? ReasonText { get; init; }
    public bool? CanApproveOnce { get; init; }
    /// <summary>KIFF's kind of hold: <c>card_unit</c> is a Card unit's exception, which Needs you leaves to its own panel.</summary>
    public string? Kind { get; init; }
    public string? TafAbout { get; init; }
    public string? TafReason { get; init; }
}

/// <summary>
/// KIFF's held calls, read and worded as the iPad's <c>KIFFClient.heldCalls</c> and <c>KIFFMapping.held</c>: the reason
/// in KIFF's own words when it sends them, else the Card's, with amounts written as the app writes money; what the call
/// is on under Details; and the taf's own words, made safe to draw.
/// </summary>
public static class KiffHeld
{
    /// <summary>The account's calls KIFF holds now.</summary>
    public const string Path = "/v1/me/card-exceptions?status=held";

    /// <summary>The arguments KIFF's gateway adds to every call, never listed again under Details.</summary>
    static readonly HashSet<string> KiffOwnArguments = ["kiff_operation_id", "kiff_about", "kiff_reason"];

    /// <summary>
    /// <c>{"exceptions": […]}</c>, as the iPad decodes it: a hold missing what it needs (its id, subject, action, entity,
    /// status, breaches or time) fails the read. The taf's words that aren't text are left out, never the hold.
    /// </summary>
    public static IReadOnlyList<KiffHold>? Read(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object || !answer.TryGetProperty("exceptions", out var list) || list.ValueKind != JsonValueKind.Array)
            return null;
        var holds = new List<KiffHold>();
        foreach (var e in list.EnumerateArray())
        {
            if (Hold(e) is not { } hold) return null;
            holds.Add(hold);
        }
        return holds;
    }

    /// <summary>One hold, as <c>GET /v1/me/card-exceptions/{id}</c> answers it, or none when it doesn't decode.</summary>
    public static KiffHold? Hold(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        string? S(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        if (S("id") is not { } id || S("subject") is not { } subject || S("action") is not { } action || S("entity_id") is not { } entity
            || S("status") is not { } status || Lenient.At(e, "created_at") is not { } created
            || !e.TryGetProperty("breaches", out var raw) || raw.ValueKind != JsonValueKind.Array)
            return null;
        var breaches = new List<HoldBreach>();
        foreach (var b in raw.EnumerateArray())
        {
            if (b.ValueKind != JsonValueKind.Object || Lenient.Text(b, "outcome") is not { } outcome) return null;
            breaches.Add(new(outcome, Lenient.Text(b, "reason"), Long(b, "limit"), Lenient.Text(b, "quantity"), Lenient.Text(b, "unit"),
                (int?)Long(b, "scale"), Flag(b, "unit_ambiguous"), Long(b, "used"), Long(b, "draw"), Lenient.Text(b, "window"),
                Lenient.Text(b, "per"), Lenient.Text(b, "value")));
        }
        Dictionary<string, long>? parameters = null;
        if (e.TryGetProperty("parameters", out var p) && p.ValueKind == JsonValueKind.Object)
            parameters = p.EnumerateObject().Where(x => x.Value.TryGetInt64(out _)).ToDictionary(x => x.Name, x => x.Value.GetInt64());
        Dictionary<string, (string, int)>? units = null;
        if (e.TryGetProperty("parameter_units", out var u) && u.ValueKind == JsonValueKind.Object)
            units = u.EnumerateObject().Where(x => Lenient.Text(x.Value, "unit") is not null && Long(x.Value, "scale") is not null)
                .ToDictionary(x => x.Name, x => (Lenient.Text(x.Value, "unit")!, (int)Long(x.Value, "scale")!.Value));
        Dictionary<string, string>? arguments = null;
        if (e.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object)
            arguments = a.EnumerateObject().ToDictionary(x => x.Name, x => ArgumentText(x.Value));
        List<string>? next = e.TryGetProperty("next_steps", out var n) && n.ValueKind == JsonValueKind.Array
            ? n.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : null;
        return new(id, subject, action, entity, status, breaches, parameters, S("reason"), units, next, created, Lenient.At(e, "expires_at"),
            (int?)Long(e, "accounting_version"))
        {
            Title = S("title"), Arguments = arguments, ProposalId = S("proposal_id"), ReasonText = S("reason_text"),
            CanApproveOnce = Flag(e, "can_approve_once"), TafAbout = S("taf_about"), TafReason = S("taf_reason"), Kind = S("kind"),
        };
    }

    static long? Long(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt64(out var l) ? l : null;

    static bool? Flag(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    /// <summary>
    /// An argument as text, as the iPad's <c>ArgumentText</c>: a string as it is, an integer or a flag written out, a decimal
    /// plainly ("0.000015", never "1.5e-05"), anything else "…".
    /// </summary>
    public static string ArgumentText(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString()!,
        JsonValueKind.Number when v.TryGetInt64(out var i) => i.ToString(CultureInfo.InvariantCulture),
        JsonValueKind.Number => Plain(v.GetDouble()),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => "…",
    };

    static string Plain(double d) => decimal.TryParse(d.ToString("G15", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var m)
        ? m.ToString(CultureInfo.InvariantCulture) : d.ToString("G15", CultureInfo.InvariantCulture);

    /// <summary>
    /// A held call's name, as the iPad's <c>KIFFMapping.says</c>: its title, with its action in words unless the title names
    /// the action's last word. planque's four letters ending a tool's name ("create_refund_repg") never show.
    /// </summary>
    public static string Says(string action, string? title)
    {
        var name = KiffActivity.Name(action, title);
        var words = CoachChat.ToolWords(action);
        var titleWords = new HashSet<string>(new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var parts = words.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!string.IsNullOrEmpty(title) && parts.Count > 1 && parts[^1] is { Length: 4 } last && !titleWords.Contains(last) && last.All(char.IsLetterOrDigit))
        {
            parts.RemoveAt(parts.Count - 1);
            words = string.Join(' ', parts);
        }
        if (parts.Count == 0) return name;
        return titleWords.Contains(parts[^1]) ? name : $"{name} · {words}";
    }

    /// <summary>
    /// The one breach a held call names, as the iPad's <c>reasonBreach</c>: over both a one-call limit and a total, and not
    /// approvable once, it's the total.
    /// </summary>
    public static HoldBreach? ReasonBreach(KiffHold e) =>
        e.CanApproveOnce == false && e.Breaches.FirstOrDefault(b => b.Outcome != "per_action_exceeded") is { } total ? total : e.Breaches.FirstOrDefault();

    /// <summary>
    /// Why it's held, as the iPad's <c>held</c>: KIFF's own words for people, else the Card's in money, else in a count, else
    /// the breach's or the hold's reason, else what the outcome means.
    /// </summary>
    public static string Reason(WordBook w, KiffHold e) =>
        (string.IsNullOrEmpty(e.ReasonText) ? null : e.ReasonText) ?? MoneyReason(w, e) ?? CountReason(w, e)
        ?? ReasonBreach(e)?.Reason ?? e.Reason ?? OutcomeText(w, ReasonBreach(e)?.Outcome);

    /// <summary>A hold over a count of calls, in the Card's words (the iPad's <c>countReason</c>); never the machine's.</summary>
    public static string? CountReason(WordBook w, KiffHold e)
    {
        if (ReasonBreach(e) is not { Outcome: "limit_reached", Quantity: "count", Limit: { } limit } b) return null;
        if (limit == 0) return w.Get("card.held.each_call_asks");
        var nth = Ordinal(w.Language, (b.Used ?? limit) + (b.Draw ?? 1));
        if (b.Window is not { } raw) return w.Get("card.over_limit_calls_would", ("limit", limit.ToString(CultureInfo.InvariantCulture)), ("nth", nth));
        var each = string.IsNullOrEmpty(b.Per) ? "" : w.Get("card.held.for_each", ("name", b.Per.ToLowerInvariant().Replace('_', ' ')));
        var item = each.Length == 0 || string.IsNullOrEmpty(b.Value) ? "" : w.Get("card.held.for_item", ("item", b.Value));
        return w.Count("card.held.count_in_window", limit,
            ("window", CardWindow.Of(w, raw).Label), ("each", each), ("nth", nth), ("item", item));
    }

    /// <summary>A hold over a money limit, its amounts written as money, when KIFF declares the limit's unit and it's the amount's.</summary>
    public static string? MoneyReason(WordBook w, KiffHold e)
    {
        if (ReasonBreach(e) is not { Limit: { } limit, Unit: { } unit } b || b.UnitAmbiguous == true || Amount(e) is not { } amount || amount.Unit != unit)
            return null;
        var cap = new KiffAmount(limit, unit, b.Scale ?? amount.Scale).Formatted(w);
        return b.Outcome switch
        {
            "per_action_exceeded" => w.Get("card.held.over_one_call", ("amount", amount.Formatted(w)), ("cap", cap)),
            "limit_reached" => w.Get("card.held.over_total", ("amount", amount.Formatted(w)), ("cap", cap)),
            _ => null,
        };
    }

    /// <summary>
    /// The amount above the answer (the iPad's <c>amount(of:)</c>): a parameter KIFF declares a unit for, in it; otherwise
    /// the parameter a breach sums, as a bare number; otherwise none.
    /// </summary>
    public static KiffAmount? Amount(KiffHold e)
    {
        if (e.Parameters is not { Count: > 0 } parameters) return null;
        var units = (e.ParameterUnits ?? new Dictionary<string, (string, int)>()).GroupBy(u => u.Key.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Value);
        var sorted = parameters.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        foreach (var p in sorted)
            if (units.TryGetValue(p.Key.ToLowerInvariant(), out var declared)) return new KiffAmount(p.Value, declared.Item1, declared.Item2);
        var summed = e.Breaches.Select(b => b.Quantity is { } q && KiffDrafts.IsSum(q) && q.EndsWith(')') ? q[4..^1].ToLowerInvariant() : null)
            .Where(s => s is not null).ToHashSet();
        return sorted.FirstOrDefault(p => summed.Contains(p.Key.ToLowerInvariant())) is { Key: not null } counted ? new KiffAmount(counted.Value, null, 0) : null;
    }

    /// <summary>
    /// What a held gateway call is on, one line an argument ("payment_intent: pi_…"), as the iPad's <c>details</c>: not the
    /// amount, which shows above, nor KIFF's own arguments.
    /// </summary>
    public static IReadOnlyList<string> Details(KiffHold e)
    {
        if (e.Arguments is not { } arguments) return [];
        var shown = new HashSet<string>((e.ParameterUnits?.Keys ?? []).Concat(e.Parameters?.Keys ?? []).Select(k => k.ToLowerInvariant()));
        return arguments.Keys.Order(StringComparer.Ordinal)
            .Where(k => !shown.Contains(k.ToLowerInvariant()) && !KiffOwnArguments.Contains(k.ToLowerInvariant()) && arguments[k].Length > 0)
            .Select(k => $"{k}: {arguments[k]}").ToList();
    }

    /// <summary>What a breach's outcome means, in the Card's words (the iPad's <c>outcomeText</c>).</summary>
    public static string OutcomeText(WordBook w, string? outcome) => w.Get(outcome switch
    {
        "per_action_exceeded" => "card.outcome.over_one_action",
        "limit_reached" => "card.outcome.over_total",
        "resource_not_on_card" => "card.outcome.not_a_resource",
        _ => "card.outcome.outside",
    });

    /// <summary>
    /// A line the taf wrote, made safe to draw (the iPad's <c>tafWords</c>): one plain line, control and format characters
    /// (Apple's control set, U+202E included) and breaks as spaces, runs of space as one, cut to <paramref name="max"/> with "…". None when nothing's left.
    /// </summary>
    public static string? TafWords(string? text, int max)
    {
        if (text is null) return null;
        var flat = new string(text.Select(c => char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format || c is (char)0x2028 or (char)0x2029 ? ' ' : c).ToArray());
        var line = string.Join(' ', flat.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (line.Length == 0) return null;
        var chars = System.Globalization.StringInfo.ParseCombiningCharacters(line);
        return chars.Length > max ? line[..chars[max - 1]] + "…" : line;
    }

    /// <summary>
    /// A held call as Needs you draws it (the iPad's <c>KIFFMapping.held</c>): its taf's name and colour, what it asks (with
    /// its entity when KIFF kept no arguments), the amount, why, when it lapses (ten minutes after it was held when KIFF
    /// doesn't say), its next steps, what it's on, and the taf's own words.
    /// </summary>
    public static HeldCall ToHeldCall(WordBook w, KiffHold e, string taf, int tafColor)
    {
        var name = Says(e.Action, e.Title);
        var summary = e.Arguments is null ? $"{name} {e.EntityId}" : name;
        return new(taf, tafColor, summary, Amount(e)?.Formatted(w), "", Reason(w, e), e.ExpiresAt ?? e.CreatedAt.AddMinutes(10),
            e.NextSteps ?? [], TafWords(e.TafAbout, 120), TafWords(e.TafReason, 280))
        {
            Details = Details(e), KiffId = e.Id,
        };
    }

    /// <summary>
    /// Needs you's held calls, as the iPad's <c>show</c>: every hold but a Card unit's, each under its taf by its agent in
    /// KIFF (<paramref name="taf"/>), else under the agent's own name.
    /// </summary>
    public static IReadOnlyList<HeldCall> Calls(WordBook w, IReadOnlyList<KiffHold> holds, Func<string, (string Name, int Colour)?> taf) =>
        holds.Where(h => h.Kind != "card_unit")
            .Select(h => taf(h.Subject) is { } t ? ToHeldCall(w, h, t.Name, t.Colour) : ToHeldCall(w, h, h.Subject, 0)).ToList();

    /// <summary>
    /// "1st", "2nd": as Apple's ordinal formatter writes digits in each shipped language, which .NET has no formatter for.
    /// A language without a rule here writes the number alone.
    /// </summary>
    public static string Ordinal(string language, long n)
    {
        var d = n.ToString(CultureInfo.InvariantCulture);
        switch (language.Split('-')[0])
        {
            case "en":
                var tens = n % 100;
                return d + (tens is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
            case "fr": return d + (n == 1 ? "er" : "e");
            case "nl": return d + "e";
            case "de" or "pl": return d + ".";
            case "es": return d + ".º";
            case "it" or "pt": return d + "º";
            default: return d;
        }
    }
}
