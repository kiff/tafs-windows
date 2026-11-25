using System.Text.Json;
using Tafs.Core.Taf;

namespace Tafs.Core.Coach;

/// <summary>
/// The Card step's shown = signed, as the iPhone's <c>CardStepCheck</c> (kiff/tafs docs/coach.md, "Coach's tools step"): the
/// plain list the step shows, each tool with its Card, its limit a day and whether past it the call waits for the person,
/// against the Cards' draft the one approval signs, compared on structured fields and failing closed. Each signed Card is read
/// on an allowlist: its id, its subject (the draft's own agent), the gateway domain, <c>on_exceed</c> (<c>owner</c> or
/// <c>refuse</c>, with a hold's expiry only on <c>owner</c>), grants that name an action and nothing else, and exactly one
/// aggregate, a count a calendar day. Anything else on a Card, a grant or the aggregate is a mismatch, and so is a tool, a
/// Card or an action on one side only.
/// </summary>
public static class CardStepCheck
{
    static readonly HashSet<string> CardKeys = ["id", "subject", "domain", "on_exceed", "hold_expiry", "grants", "aggregates"];
    static readonly HashSet<string> AggregateKeys = ["quantity", "limit", "window"];
    static readonly HashSet<string> ConnectionKeys = ["name", "url", "tool", "mode", "audience", "control", "idempotency_argument", "read_only", "replace"];

    /// <summary>One tool as signed: its Card, its action, its count a day, and whether past it the call waits for the person.</summary>
    public sealed record Signed(string Card, string Tool, int Limit, bool Asks);

    /// <summary>Whether the plain list the step shows is exactly what <paramref name="draft"/> signs; false on anything else.</summary>
    public static bool Matches(IReadOnlyList<StepGroup> groups, byte[] draft, string agent) =>
        Shown(groups) is { Count: > 0 } shown && SignedBy(draft, agent) is { } signed && shown.SetEquals(signed);

    /// <summary>
    /// What the step shows as signed by this draft, one entry per tool (those <c>in_draft</c>, or every one from a tafs cloud
    /// that doesn't mark them); none when such a tool doesn't say all of it, or a tool is listed twice.
    /// </summary>
    public static HashSet<Signed>? Shown(IReadOnlyList<StepGroup> groups)
    {
        var shown = new HashSet<Signed>();
        var tools = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in groups.SelectMany(g => g.Tools).Where(t => t.InDraft != false))
        {
            if (t.Tool is not { Length: > 0 } tool || t.Card is not { Length: > 0 } card || t.Limit is not { } limit || limit < 0
                || t.Asks is not { } asks || !tools.Add(tool.ToLowerInvariant())) return null;
            shown.Add(new(card, tool.ToLowerInvariant(), limit, asks));
        }
        return shown;
    }

    /// <summary>What the draft signs, one entry per Card and action; none when anything on it is outside the allowlist.</summary>
    public static HashSet<Signed>? SignedBy(byte[] draft, string agent)
    {
        JsonElement body;
        try { body = JsonDocument.Parse(draft).RootElement.Clone(); }
        catch (JsonException) { return null; }
        if (body.ValueKind != JsonValueKind.Object || Lenient.Field(body, "cards") is not { ValueKind: JsonValueKind.Array } cards
            || cards.GetArrayLength() == 0) return null;
        var owner = Lenient.Field(body, "agent_id") is { } a ? (a.ValueKind == JsonValueKind.String ? a.GetString()! : null) : agent;
        if (owner is not { Length: > 0 } || (agent.Length > 0 && owner != agent)) return null;
        var signed = new HashSet<Signed>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var actions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in cards.EnumerateArray())
        {
            if (card.ValueKind != JsonValueKind.Object || !card.EnumerateObject().All(p => CardKeys.Contains(p.Name))
                || Lenient.Text(card, "id") is not { Length: > 0 } id || !ids.Add(id)
                || Lenient.Text(card, "subject") != owner || Lenient.Text(card, "domain") != "gateway"
                || Lenient.Text(card, "on_exceed") is not ("owner" or "refuse") || Lenient.Text(card, "on_exceed") is not { } onExceed) return null;
            if (Lenient.Field(card, "hold_expiry") is { } expiry && (onExceed != "owner" || expiry.ValueKind != JsonValueKind.String)) return null;
            if (Lenient.Field(card, "aggregates") is not { ValueKind: JsonValueKind.Array } aggregates || aggregates.GetArrayLength() != 1) return null;
            var aggregate = aggregates[0];
            if (aggregate.ValueKind != JsonValueKind.Object || !aggregate.EnumerateObject().Select(p => p.Name).ToHashSet().SetEquals(AggregateKeys)
                || Lenient.Text(aggregate, "quantity") != "count" || Lenient.Text(aggregate, "window") != "calendar_day"
                || Lenient.Field(aggregate, "limit") is not { ValueKind: JsonValueKind.Number } l
                || !l.TryGetDecimal(out var limitValue) || limitValue != decimal.Truncate(limitValue) || limitValue < 0 || limitValue > int.MaxValue) return null;
            var limit = (int)limitValue;
            if (Lenient.Field(card, "grants") is not { ValueKind: JsonValueKind.Array } grants || grants.GetArrayLength() == 0) return null;
            foreach (var grant in grants.EnumerateArray())
            {
                if (grant.ValueKind != JsonValueKind.Object || grant.EnumerateObject().Select(p => p.Name).ToList() is not ["action"]
                    || Lenient.Text(grant, "action") is not { Length: > 0 } action || !actions.Add(action.ToLowerInvariant())) return null;
                signed.Add(new(id, action.ToLowerInvariant(), limit, onExceed == "owner"));
            }
        }
        return signed;
    }

    /// <summary>
    /// The tools batch the same approval signs (<c>POST /v1/connections/batch</c>), held to the step: each connection is one of
    /// the step's tools (its <c>name</c> the tool's), connected as Coach's own control server at this app's tafs cloud
    /// (<c>url</c> and <c>audience</c> both <paramref name="mcp"/>, <c>control: true</c>, <c>mode: verify</c>), with only the keys
    /// tafs cloud writes (<c>read_only</c> and <c>replace</c> only as true). Anything else, a tool twice, or an empty batch is
    /// a mismatch, and nothing is signed.
    /// </summary>
    public static bool BatchMatches(IReadOnlyList<StepGroup> groups, byte[] batch, string mcp)
    {
        var tools = groups.SelectMany(g => g.Tools).Select(t => t.Tool?.ToLowerInvariant()).OfType<string>().ToHashSet(StringComparer.Ordinal);
        if (tools.Count == 0 || mcp.Length == 0) return false;
        JsonElement body;
        try { body = JsonDocument.Parse(batch).RootElement.Clone(); }
        catch (JsonException) { return false; }
        if (body.ValueKind != JsonValueKind.Object || body.EnumerateObject().Select(p => p.Name).ToList() is not ["connections"]
            || Lenient.Field(body, "connections") is not { ValueKind: JsonValueKind.Array } connections || connections.GetArrayLength() == 0) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in connections.EnumerateArray())
        {
            if (c.ValueKind != JsonValueKind.Object || !c.EnumerateObject().All(p => ConnectionKeys.Contains(p.Name))
                || Lenient.Text(c, "tool") is not { } tool || Lenient.Text(c, "name") != tool || !tools.Contains(tool.ToLowerInvariant())
                || !seen.Add(tool.ToLowerInvariant()) || Lenient.Text(c, "url") != mcp || Lenient.Text(c, "audience") != mcp
                || Lenient.Text(c, "mode") != "verify" || Lenient.Field(c, "control") is not { ValueKind: JsonValueKind.True }
                || Lenient.Text(c, "idempotency_argument") is not { Length: > 0 }) return false;
            foreach (var key in new[] { "read_only", "replace" })
                if (Lenient.Field(c, key) is { } v && v.ValueKind != JsonValueKind.True) return false;
        }
        return true;
    }
}
