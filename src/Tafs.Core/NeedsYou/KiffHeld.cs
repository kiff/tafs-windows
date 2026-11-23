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
    /// <summary>
    /// A browser step that fills a sealed value (its <c>browser</c> block carries <c>sealed_fill</c> or <c>fill</c>): allowed only
    /// with the paired phone's release (docs/chrome.md, "never an allowing answer without its release"), which a PC can't make.
    /// </summary>
    public bool NeedsRelease { get; init; }
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
    /// <c>{"exceptions": […]}</c>, as the iPad's synthesized decoder reads <c>CardException</c>: a hold missing what it needs
    /// (its id, subject, action, entity, status, breaches or time), or any field of the wrong kind where it's there and not
    /// null, fails the read. Only the taf's words (the iPad's <c>LenientItem</c>) and the arguments (<c>ArgumentText</c>, which
    /// never fails) are read leniently.
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
        if (!Fits(e)) return null;
        string? S(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var breaches = e.GetProperty("breaches").EnumerateArray().Select(b => new HoldBreach(S2(b, "outcome")!, S2(b, "reason"), Long(b, "limit"),
            S2(b, "quantity"), S2(b, "unit"), (int?)Long(b, "scale"), Flag(b, "unit_ambiguous"), Long(b, "used"), Long(b, "draw"),
            S2(b, "window"), S2(b, "per"), S2(b, "value"))).ToList();
        Dictionary<string, long>? parameters = e.TryGetProperty("parameters", out var p) && p.ValueKind == JsonValueKind.Object
            ? p.EnumerateObject().ToDictionary(x => x.Name, x => Whole(x.Value)) : null;
        Dictionary<string, (string, int)>? units = e.TryGetProperty("parameter_units", out var u) && u.ValueKind == JsonValueKind.Object
            ? u.EnumerateObject().ToDictionary(x => x.Name, x => (x.Value.GetProperty("unit").GetString()!, (int)Whole(x.Value.GetProperty("scale")))) : null;
        Dictionary<string, string>? arguments = e.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object
            ? a.EnumerateObject().ToDictionary(x => x.Name, x => ArgumentText(x.Value)) : null;
        List<string>? next = e.TryGetProperty("next_steps", out var n) && n.ValueKind == JsonValueKind.Array
            ? n.EnumerateArray().Select(x => x.GetString()!).ToList() : null;
        return new(S("id")!, S("subject")!, S("action")!, S("entity_id")!, S("status")!, breaches, parameters, S("reason"), units, next,
            KiffActivity.Parse(S("created_at")!)!.Value, S("expires_at") is { } expires ? KiffActivity.Parse(expires) : null,
            (int?)Long(e, "accounting_version"))
        {
            Title = S("title"), Arguments = arguments, ProposalId = S("proposal_id"), ReasonText = S("reason_text"),
            CanApproveOnce = Flag(e, "can_approve_once"), TafAbout = S("taf_about"), TafReason = S("taf_reason"), Kind = S("kind"),
            NeedsRelease = Fill(e),
        };
    }

    /// <summary>
    /// A hold's shape as Swift's decoder checks <c>CardException</c> (KIFFClient.swift): the required fields there and of
    /// their kind, every optional one absent, null or of its kind, times as ISO 8601 with an offset or Z.
    /// </summary>
    static bool Fits(JsonElement e)
    {
        static bool At(JsonElement v) => KiffActivity.IsAt(v);
        static bool Object(JsonElement v) => v.ValueKind == JsonValueKind.Object;
        static bool Breach(JsonElement b) =>
            Shape.Required(b, "outcome", Shape.Text) && new[] { "reason", "quantity", "unit", "window", "per", "value" }.All(f => Shape.Optional(b, f, Shape.Text))
            && new[] { "limit", "scale", "used", "draw" }.All(f => Shape.Optional(b, f, Shape.Int))
            && new[] { "unit_unavailable", "unit_ambiguous" }.All(f => Shape.Optional(b, f, Shape.Bool));
        return Object(e)
            && new[] { "id", "subject", "action", "entity_id", "status" }.All(f => Shape.Required(e, f, Shape.Text))
            && Shape.Required(e, "created_at", At) && Shape.Required(e, "breaches", v => Shape.List(v, Breach))
            && Shape.Optional(e, "parameters", v => Object(v) && v.EnumerateObject().All(x => Shape.Int(x.Value)))
            && Shape.Optional(e, "parameter_units", v => Object(v) && v.EnumerateObject().All(x =>
                Shape.Required(x.Value, "unit", Shape.Text) && Shape.Required(x.Value, "scale", Shape.Int)))
            && Shape.Optional(e, "next_steps", v => Shape.List(v, Shape.Text))
            && Shape.Optional(e, "expires_at", At) && Shape.Optional(e, "last_at", At)
            && new[] { "reason", "title", "proposal_id", "reason_text", "kind" }.All(f => Shape.Optional(e, f, Shape.Text))
            && new[] { "accounting_version", "count" }.All(f => Shape.Optional(e, f, Shape.Int))
            && new[] { "can_approve_once", "units_unavailable" }.All(f => Shape.Optional(e, f, Shape.Bool))
            && Shape.Optional(e, "arguments", Object);
    }

    static string? S2(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>A whole number as Swift reads an <c>Int64</c>: 40 and 40.0 alike (<see cref="Shape.Int"/> checked it's whole).</summary>
    static long Whole(JsonElement v) => v.TryGetInt64(out var l) ? l : (long)v.GetDouble();

    static long? Long(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? Whole(v) : null;

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
    /// <summary>
    /// Coach's hold of a version to install, as the iPad's <c>KIFFMapping.install(of:coach:)</c>: Coach's own agent, the action
    /// <c>version_install</c>, a <c>taf</c> argument and a version <c>n</c> above 0. Anything else is an ordinary held call.
    /// </summary>
    public static VersionInstall? InstallOf(KiffHold e) =>
        e.Subject == SetupAgent && e.Action == "version_install" && e.Arguments is { } args
        && args.TryGetValue("taf", out var taf) && taf.Length > 0
        && args.TryGetValue("n", out var n) && int.TryParse(n, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) && number > 0
            ? new VersionInstall(taf, number) : null;

    /// <summary>
    /// Whether the hold's <c>browser</c> block asks for a fill (<c>sealed_fill</c>, or <c>fill</c>): read as anything there but null,
    /// so a fill in a shape this app doesn't know still counts as one, never as a plain step to allow. A <c>browser</c> block
    /// that isn't an object (it can't be read) counts too, as on Linux and the Mac: never allowed here on a guess.
    /// </summary>
    static bool Fill(JsonElement e) =>
        e.TryGetProperty("browser", out var b) && b.ValueKind != JsonValueKind.Null
        && (b.ValueKind != JsonValueKind.Object || new[] { "sealed_fill", "fill" }.Any(k => b.TryGetProperty(k, out var f) && f.ValueKind != JsonValueKind.Null));

    public static HeldCall ToHeldCall(WordBook w, KiffHold e, string taf, int tafColor)
    {
        var name = Says(e.Action, e.Title);
        var summary = e.Arguments is null ? $"{name} {e.EntityId}" : name;
        return new(taf, tafColor, summary, Amount(e)?.Formatted(w), "", Reason(w, e), e.ExpiresAt ?? e.CreatedAt.AddMinutes(10),
            e.NextSteps ?? [], TafWords(e.TafAbout, 120), TafWords(e.TafReason, 280))
        {
            Details = Details(e), KiffId = e.Id, ProposalId = e.ProposalId, AccountingVersion = e.AccountingVersion, ApprovableOnce = e.CanApproveOnce ?? true,
            Action = e.Action, Agent = e.Subject, BreachOutcomes = e.Breaches.Select(b => b.Outcome).ToList(),
            Install = InstallOf(e), NeedsRelease = e.NeedsRelease,
        };
    }

    /// <summary>
    /// Needs you's held calls, as the iPad's <c>show</c>: every hold but a Card unit's, each under its taf by its agent in
    /// KIFF (<paramref name="taf"/>), else under the agent's own name.
    /// </summary>
    /// <summary>
    /// The held calls Needs you shows, in KIFF's order, Card units' left to their own panel. Each under its holder, as the
    /// iPad's <c>holderName(…) ?? aTaf</c>: the taf on Home's list by its agent in KIFF, with its avatar; Coach for Setup's
    /// agent; else "A taf", with no avatar. Only a taf's calls are listed in the sidebar (the iPad's HomeSplitView).
    /// </summary>
    public static IReadOnlyList<HeldCall> Calls(WordBook w, IReadOnlyList<KiffHold> holds, Func<string, (string Name, int Colour)?> taf) =>
        holds.Where(h => h.Kind != "card_unit").Select(h => taf(h.Subject) is { } t
            ? ToHeldCall(w, h, t.Name, t.Colour)
            : ToHeldCall(w, h, w.Get(h.Subject == SetupAgent ? "coach.title" : "needs_you.a_taf"), 0) with { Listed = false, Avatar = h.Subject == SetupAgent }).ToList();

    /// <summary>Coach's agent in KIFF (the iPad's <c>setupAgent</c>).</summary>
    public const string SetupAgent = "setup";

    /// <summary>
    /// "1st", "2nd": as Apple's ordinal formatter (ICU's rule-based numbers) writes digits in each language, which .NET has
    /// no formatter for. The same table as tafs-linux's <c>held::ordinal</c>, checked there against ICU. Digits are Latin.
    /// </summary>
    public static string Ordinal(string language, long n)
    {
        var d = n.ToString(CultureInfo.InvariantCulture);
        var n10 = n % 10;
        var n100 = n % 100;
        switch (language.Split('-')[0])
        {
            case "en":
                return d + (n100 is >= 11 and <= 13 ? "th" : n10 switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
            case "fr": return n == 1 ? "1er" : d + "e";
            case "nl": return d + "e";
            case "es": return d + ".º";
            case "it" or "pt": return d + "º";
            case "ja" or "zh": return "第" + d;
            case "ko": return d + "번째";
            case "id": return "ke-" + d;
            case "vi": return "thứ " + d;
            case "th": return "ที่ " + d;
            case "ro": return d + "a";
            case "bg":
                var bg = n100 == 0 && n % 1000 != 0 ? "тен"
                    : n100 is >= 11 and <= 19 ? "и"
                    : n10 switch { 1 => "ви", 2 => "ри", 3 or 4 => "ти", _ => "и" };
                return d + "-" + bg;
            case "ca":
                var ca = n switch
                {
                    1 or 3 => "r",
                    2 => "n",
                    4 => "t",
                    _ when n100 == 1 && n > 100 => "r",
                    _ when n100 == 2 && n > 100 => "n",
                    _ when n10 == 1 && n100 != 11 && n > 20 => "r",
                    _ when n10 == 2 && n100 != 12 && n > 20 => "n",
                    _ => "è",
                };
                return d + ca;
            // ICU's own data: Mongolian takes the English "th", Lao Thai's "ที่", Khmer its "ទី".
            case "mn": return d + "th";
            case "km": return "ទី" + d;
            case "lo": return "ที่​" + d;
            case "az": return d + "'inci";
            // ICU's Bengali: English's suffixes by CLDR's Bengali ordinal classes.
            case "bn":
                return d + (n switch { 1 or 5 or 7 or 8 or 9 or 10 => "st", 2 or 3 => "nd", 4 => "rd", 6 => "th", _ => "th" });
            case "ar" or "he": return d;
            default: return d + ".";
        }
    }
}
