using System.Globalization;
using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Record;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Approvals;

/// <summary>One cap on each call of a grant: the quantity it caps (<c>sum(amount)</c>, or a count), its most, and its unit and scale.</summary>
public sealed record DraftCap(string Quantity, long Max, string? Unit = null, int? Scale = null);

/// <summary>One action a draft's Card grants: its action, its title for people, and its caps on each call.</summary>
public sealed record DraftGrant(string Action, string? Title = null, IReadOnlyList<DraftCap>? PerActionMax = null);

/// <summary>A total over a window: the quantity, its limit, KIFF's window, and its unit and scale.</summary>
public sealed record DraftTotal(string Quantity, long Limit, string Window, string? Unit = null, int? Scale = null);

/// <summary>
/// A Card's terms as the person reads them before confirming, as the iPad's <c>DraftCard</c>: each action and its caps, the
/// totals, what KIFF does past a limit (<c>owner</c> asks, <c>refuse</c> refuses), and KIFF's own lines when it sends them.
/// </summary>
public sealed record DraftCard(IReadOnlyList<DraftGrant>? Grants, IReadOnlyList<DraftTotal>? Totals, string? OnExceed = null,
    IReadOnlyList<string>? KiffLines = null, string? Revises = null, DraftCard? Current = null, bool? Unchanged = null)
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
    /// and total in its shape, and one it can't read fails the draft. Fields KIFF adds later are no matter.
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
            && Shape.Optional(c, "unit", Shape.Text) && Shape.Optional(c, "scale", Shape.Int)))
        && Shape.Optional(g, "resources", r => Shape.Required(r, "parameter", Shape.Text) && Shape.Optional(r, "values", v => Shape.List(v, Shape.Text)))
        && Shape.Optional(g, "entity_states", v => Shape.List(v, Shape.Text)) && Shape.Optional(g, "next_steps", v => Shape.List(v, Shape.Text));

    static bool IsTotal(JsonElement a) =>
        Shape.Required(a, "quantity", Shape.Text) && Shape.Required(a, "limit", Shape.Int) && Shape.Required(a, "window", Shape.Text)
        && Shape.Optional(a, "unit", Shape.Text) && Shape.Optional(a, "scale", Shape.Int)
        && Shape.Optional(a, "measure", Shape.Text) && Shape.Optional(a, "per", Shape.Text);

    static DraftCard Card(JsonElement c)
    {
        IReadOnlyList<T>? List<T>(string n, Func<JsonElement, T> each) =>
            Lenient.Field(c, n) is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray().Select(each).ToList() : null;
        return new DraftCard(
            List("grants", g => new DraftGrant(Lenient.Text(g, "action")!, Lenient.Text(g, "title"),
                Lenient.Field(g, "per_action_max") is { ValueKind: JsonValueKind.Array } caps
                    ? caps.EnumerateArray().Select(x => new DraftCap(Lenient.Text(x, "quantity")!, Lenient.Long(x, "max") ?? (long)x.GetProperty("max").GetDouble(),
                        Lenient.Text(x, "unit"), Lenient.Int(x, "scale"))).ToList()
                    : null)),
            List("aggregates", a => new DraftTotal(Lenient.Text(a, "quantity")!, Lenient.Long(a, "limit") ?? (long)a.GetProperty("limit").GetDouble(),
                Lenient.Text(a, "window")!, Lenient.Text(a, "unit"), Lenient.Int(a, "scale"))),
            Lenient.Text(c, "on_exceed"), List("lines", l => l.GetString()!), Lenient.Text(c, "revises"),
            Lenient.Field(c, "current") is { ValueKind: JsonValueKind.Object } cur ? Card(cur) : null,
            Lenient.Field(c, "unchanged") is { ValueKind: JsonValueKind.True or JsonValueKind.False } u ? u.GetBoolean() : null);
    }
}
