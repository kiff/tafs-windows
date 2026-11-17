using System.Globalization;
using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Record;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Approvals;

/// <summary>One cap on each call of a grant: the quantity it caps (<c>sum(amount)</c>, or a count), its most, and its unit and scale.</summary>
public sealed record DraftCap(string Quantity, long Max, string? Unit = null, int? Scale = null);

/// <summary>What a grant may act on, as the iPad's <c>DraftCard.Grant.Resources</c>: only these values of one parameter.</summary>
public sealed record DraftResources(string Parameter, IReadOnlyList<string>? Values = null);

/// <summary>
/// One action a draft's Card grants: its action, its title for people, and its caps on each call; and, read so a draft can be
/// compared with the Card in force (the iPad's <c>HoldAllow</c>), its scope, the states it applies in, and its next steps.
/// </summary>
public sealed record DraftGrant(string Action, string? Title = null, IReadOnlyList<DraftCap>? PerActionMax = null,
    DraftResources? Resources = null, IReadOnlyList<string>? EntityStates = null, IReadOnlyList<string>? NextSteps = null);

/// <summary>
/// A total over a window: the quantity, its limit, KIFF's window, and its unit and scale; what it counts (attempts or effects,
/// KIFF's default when left out), and the parameter it's kept for each value of, when it's per item.
/// </summary>
public sealed record DraftTotal(string Quantity, long Limit, string Window, string? Unit = null, int? Scale = null,
    string? Measure = null, string? Per = null);

/// <summary>
/// A Card's terms as the person reads them before confirming, as the iPad's <c>DraftCard</c>: each action and its caps, the
/// totals, what KIFF does past a limit (<c>owner</c> asks, <c>refuse</c> refuses), and KIFF's own lines when it sends them.
/// Its other terms, as KIFF writes them, are read so a draft can be compared with the Card in force (the iPad's
/// <c>HoldAllow</c>): who holds it, where it applies, when it starts and ends, and a draft's own <c>hold_expiry</c> and
/// <c>revoked_at</c>.
/// </summary>
public sealed record DraftCard(IReadOnlyList<DraftGrant>? Grants, IReadOnlyList<DraftTotal>? Totals, string? OnExceed = null,
    IReadOnlyList<string>? KiffLines = null, string? Revises = null, DraftCard? Current = null, bool? Unchanged = null,
    string? Subject = null, IReadOnlyList<string>? Subjects = null, string? Domain = null, string? Environment = null,
    string? Location = null, string? ValidFrom = null, string? ValidUntil = null, string? HoldExpiry = null, string? RevokedAt = null)
{
    public bool AsksPastLimits => OnExceed == "owner";
    public bool IsUnchanged => Unchanged == true && Current is not null;

    /// <summary>
    /// The lines the person approves, as the iPad's <c>DraftCard.lines</c>: KIFF's own when sent; else each action by its
    /// title ("Refund a payment: up to €100 each"), with its action in words where two read alike, then the totals ("€1,000 in
    /// total a day", "20 calls a day"); on a Card that asks past its limits, "; more asks you first", ", then it asks you",
    /// and a count of 0 is "Each call asks you first". An amount's unit is KIFF's, or the number stays bare.
    /// </summary>
    public IReadOnlyList<string> Lines(WordBook w, IReadOnlySet<string>? alike = null)
    {
        if (KiffLines is { Count: > 0 } kiff) return kiff;
        alike ??= KiffDrafts.Alike(Grants ?? []);
        var lines = new List<string>();
        var more = AsksPastLimits ? w.Get("card.line.more_asks") : "";
        foreach (var g in Grants ?? [])
        {
            var name = KiffActivity.Name(g.Action, g.Title);
            var caps = (g.PerActionMax ?? []).Select(cap => w.Get("card.line.up_to_each", ("amount",
                KiffDrafts.IsSum(cap.Quantity) ? KiffDrafts.Money(cap.Max, cap.Unit, cap.Scale).Formatted(w) : cap.Max.ToString(CultureInfo.InvariantCulture)))).ToList();
            var action = alike.Contains(name.ToLowerInvariant()) ? $"{name} · {CoachChat.ToolWords(g.Action)}" : name;
            lines.Add(caps.Count == 0 ? action : $"{action}: {string.Join(", ", caps)}{more}");
        }
        var then = AsksPastLimits ? w.Get("card.line.then_asks") : "";
        foreach (var t in Totals ?? [])
        {
            // KIFF's own window, exactly: never a lifetime or hourly total read as a day's.
            var window = CardWindow.Of(w, t.Window);
            if (KiffDrafts.IsSum(t.Quantity)) lines.Add($"{KiffDrafts.Money(t.Limit, t.Unit, t.Scale).Formatted(w)} {window.SumLabel(w)}{then}");
            else if (AsksPastLimits && t.Limit == 0) lines.Add(w.Get("card.held.each_call_asks"));
            else lines.Add(w.Count("card.line.calls_in_window", t.Limit, ("window", window.Label), ("then", then)));
        }
        return lines;
    }
}

/// <summary>
/// A draft of a taf's Cards in KIFF, as the iPad's <c>AgentDraft</c>: its status (<c>draft</c>, <c>confirming</c>,
/// <c>confirmed</c>, <c>claimed</c>…), the version a confirmation signs, the Cards it would issue, and the name it was
/// submitted with. A claim secret is never kept here.
/// </summary>
public sealed record AgentDraft(string Id, string AgentId, string Status, string Version, IReadOnlyList<DraftCard>? Cards = null,
    string? Label = null, string? Reason = null, IReadOnlyList<string>? CardIds = null, bool? NoTools = null,
    IReadOnlyList<(string Id, DraftCard? Current)>? Retires = null, string? FromHold = null)
{
    public bool Confirmed => Status == "confirmed";
    /// <summary>KIFF took the signature for it: confirmed, or confirmed and its key claimed since.</summary>
    public bool Signed => Status is "confirmed" or "claimed";

    /// <summary>Each Card's lines, read together: two grants that read alike, on one Card or two, each add their action.</summary>
    public IReadOnlyList<IReadOnlyList<string>> CardLines(WordBook w)
    {
        var all = Cards ?? [];
        var alike = KiffDrafts.Alike(all.SelectMany(c => c.Grants ?? []));
        return all.Select(c => c.Lines(w, alike)).ToList();
    }

    /// <summary>"Refunds assistant's Card": the name it was submitted with, never its agent id; else "The taf's Card".</summary>
    public string CardTitle(WordBook w) =>
        Label?.Trim() is { Length: > 0 } name ? w.Get("coach.card_of", ("owner", name)) : w.Get("card.the_tafs_card");
}

/// <summary>KIFF's drafts, as the iPad's <c>KIFFClient.draft</c>: <c>GET /v1/me/agent-drafts/{id}</c> as the session.</summary>
public static class KiffDrafts
{
    public static string Path(string id) => "/v1/me/agent-drafts/" + CloudRead.PathPart(id);

    /// <summary>
    /// A draft as KIFF answered it, or why it couldn't be read, as the iPad's <c>readCardDraft</c> keeps its
    /// <c>setupDraftErrors</c>: KIFF's own reason when it refused or wasn't reached, "KIFF didn't answer as expected" for an
    /// answer it can't read.
    /// </summary>
    public static (AgentDraft? Draft, string? Why) Read(WordBook w, CloudRead read) =>
        read.Answer is { } d && Read(d) is { } draft ? (draft, null)
            : (null, read.Answer is null && read.Why is { } why ? why : w.Get("kiff.kiff_did_not_answer_expected"));

    /// <summary>A quantity that adds an amount up (<c>sum(amount_cents)</c>), not a count.</summary>
    public static bool IsSum(string quantity) => quantity.StartsWith("sum(", StringComparison.OrdinalIgnoreCase);

    /// <summary>An amount in KIFF's unit and scale, or bare with none, as the iPad's <c>KIFFMapping.money</c>.</summary>
    public static KiffAmount Money(long minor, string? unit, int? scale) =>
        string.IsNullOrEmpty(unit) ? new KiffAmount(minor, null, 0) : new KiffAmount(minor, unit, scale ?? 0);

    /// <summary>The names, ignoring case, that more than one of these grants would read as.</summary>
    public static IReadOnlySet<string> Alike(IEnumerable<DraftGrant> grants) =>
        grants.GroupBy(g => KiffActivity.Name(g.Action, g.Title).ToLowerInvariant()).Where(n => n.Count() > 1).Select(n => n.Key).ToHashSet();

    /// <summary>
    /// A draft, read as strictly as the iPad's decoder: its id, agent, status and version are required; each Card, grant, cap
    /// and total in its shape, and one it can't read fails the draft; so does a scale past <see cref="KiffAmount.MaxScale"/>.
    /// Fields KIFF adds later are no matter.
    /// </summary>
    public static AgentDraft? Read(JsonElement d)
    {
        string[] required = ["id", "agent_id", "status", "version"];
        string[] optional = ["claim_secret", "reason", "label", "from_hold"];
        if (!required.All(n => Shape.Required(d, n, Shape.Text)) || !optional.All(n => Shape.Optional(d, n, Shape.Text))
            || !Shape.Optional(d, "card_ids", v => Shape.List(v, Shape.Text)) || !Shape.Optional(d, "no_tools", Shape.Bool)
            || !Shape.Optional(d, "cards", v => Shape.List(v, c => IsTerms(c, card: true)))
            || !Shape.Optional(d, "retires", v => Shape.List(v, r => Shape.Required(r, "id", Shape.Text) && Shape.Optional(r, "current", c => IsTerms(c, card: false)))))
            return null;
        string? T(string n) => Lenient.Text(d, n);
        return new AgentDraft(T("id")!, T("agent_id")!, T("status")!, T("version")!,
            Lenient.Field(d, "cards") is { ValueKind: JsonValueKind.Array } cards ? cards.EnumerateArray().Select(Card).ToList() : null,
            T("label"), T("reason"),
            Lenient.Field(d, "card_ids") is { ValueKind: JsonValueKind.Array } ids ? ids.EnumerateArray().Select(i => i.GetString()!).ToList() : null,
            Lenient.Field(d, "no_tools") is { ValueKind: JsonValueKind.True or JsonValueKind.False } nt ? nt.GetBoolean() : null,
            Lenient.Field(d, "retires") is { ValueKind: JsonValueKind.Array } r
                ? r.EnumerateArray().Select(x => (Lenient.Text(x, "id")!, Lenient.Field(x, "current") is { ValueKind: JsonValueKind.Object } c ? Card(c) : null)).ToList()
                : null,
            T("from_hold"));
    }

    static readonly string[] TermWords = ["on_exceed", "subject", "domain", "environment", "location", "valid_from", "valid_until"];

    /// <summary>A Card's terms in their shape, as the iPad's <c>DraftCard</c> (with <paramref name="card"/>) or <c>Terms</c>.</summary>
    static bool IsTerms(JsonElement c, bool card) =>
        c.ValueKind == JsonValueKind.Object && TermWords.All(n => Shape.Optional(c, n, Shape.Text))
        && Shape.Optional(c, "lines", v => Shape.List(v, Shape.Text)) && Shape.Optional(c, "subjects", v => Shape.List(v, Shape.Text))
        && Shape.Optional(c, "grants", v => Shape.List(v, IsGrant)) && Shape.Optional(c, "aggregates", v => Shape.List(v, IsTotal))
        && (!card || (new[] { "revises", "hold_expiry", "revoked_at" }.All(n => Shape.Optional(c, n, Shape.Text))
            && Shape.Optional(c, "unchanged", Shape.Bool) && Shape.Optional(c, "current", t => IsTerms(t, card: false))));

    static bool IsGrant(JsonElement g) =>
        Shape.Required(g, "action", Shape.Text) && Shape.Optional(g, "title", Shape.Text)
        && Shape.Optional(g, "per_action_max", v => Shape.List(v, c => Shape.Required(c, "quantity", Shape.Text) && Shape.Required(c, "max", Shape.Int)
            && Shape.Optional(c, "unit", Shape.Text) && Shape.Optional(c, "scale", IsScale)))
        && Shape.Optional(g, "resources", r => Shape.Required(r, "parameter", Shape.Text) && Shape.Optional(r, "values", v => Shape.List(v, Shape.Text)))
        && Shape.Optional(g, "entity_states", v => Shape.List(v, Shape.Text)) && Shape.Optional(g, "next_steps", v => Shape.List(v, Shape.Text));

    static bool IsTotal(JsonElement a) =>
        Shape.Required(a, "quantity", Shape.Text) && Shape.Required(a, "limit", Shape.Int) && Shape.Required(a, "window", Shape.Text)
        && Shape.Optional(a, "unit", Shape.Text) && Shape.Optional(a, "scale", IsScale)
        && Shape.Optional(a, "measure", Shape.Text) && Shape.Optional(a, "per", Shape.Text);

    /// <summary>
    /// A scale an amount can be written with (<see cref="KiffAmount.MaxScale"/>): a draft with one past it isn't shown, so a
    /// Card is never confirmed with an amount the person couldn't read.
    /// </summary>
    static bool IsScale(JsonElement s) => Shape.Int(s) && s.GetDouble() <= KiffAmount.MaxScale;

    static DraftCard Card(JsonElement c)
    {
        static IReadOnlyList<T>? List<T>(JsonElement o, string n, Func<JsonElement, T> each) =>
            Lenient.Field(o, n) is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray().Select(each).ToList() : null;
        static IReadOnlyList<string>? Texts(JsonElement o, string n) => List(o, n, t => t.GetString()!);
        string? T(string n) => Lenient.Text(c, n);
        return new DraftCard(
            List(c, "grants", g => new DraftGrant(Lenient.Text(g, "action")!, Lenient.Text(g, "title"),
                List(g, "per_action_max", x => new DraftCap(Lenient.Text(x, "quantity")!, Lenient.Long(x, "max") ?? (long)x.GetProperty("max").GetDouble(),
                    Lenient.Text(x, "unit"), Lenient.Int(x, "scale"))),
                Lenient.Field(g, "resources") is { ValueKind: JsonValueKind.Object } r ? new DraftResources(Lenient.Text(r, "parameter")!, Texts(r, "values")) : null,
                Texts(g, "entity_states"), Texts(g, "next_steps"))),
            List(c, "aggregates", a => new DraftTotal(Lenient.Text(a, "quantity")!, Lenient.Long(a, "limit") ?? (long)a.GetProperty("limit").GetDouble(),
                Lenient.Text(a, "window")!, Lenient.Text(a, "unit"), Lenient.Int(a, "scale"), Lenient.Text(a, "measure"), Lenient.Text(a, "per"))),
            T("on_exceed"), Texts(c, "lines"), T("revises"),
            Lenient.Field(c, "current") is { ValueKind: JsonValueKind.Object } cur ? Card(cur) : null,
            Lenient.Field(c, "unchanged") is { ValueKind: JsonValueKind.True or JsonValueKind.False } u ? u.GetBoolean() : null,
            T("subject"), Texts(c, "subjects"), T("domain"), T("environment"), T("location"), T("valid_from"), T("valid_until"),
            T("hold_expiry"), T("revoked_at"));
    }
}
