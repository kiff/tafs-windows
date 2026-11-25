using System.Text.Json;

namespace Tafs.Core.NeedsYou;

/// <summary>
/// "Allow this from now on", its second half, as the iPad's <c>HoldAllow.keysKnown</c>: every key of each of KIFF's draft's
/// Cards, as KIFF sent them, is one the check reads, so a term KIFF adds later is never signed unseen. The keys are kiff/tafs'
/// <c>vectors/agent-draft-keys.json</c>, by object, the same on every app; its cases are this PC's tests.
/// </summary>
public static class HoldAllowKeys
{
    /// <summary>The keys each object of a draft's Cards may carry: those <see cref="HoldAllow.Matches"/> compares, and the few it reads as words.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Keys = new Dictionary<string, IReadOnlySet<string>>
    {
        ["card"] = new HashSet<string> { "id", "revises", "current", "unchanged", "grants", "aggregates", "on_exceed", "lines", "subject",
            "subjects", "domain", "environment", "location", "valid_from", "valid_until", "hold_expiry", "revoked_at" },
        ["current"] = new HashSet<string> { "id", "grants", "aggregates", "on_exceed", "lines", "subject", "subjects", "domain",
            "environment", "location", "valid_from", "valid_until", "active", "domain_id", "hold_expiry_seconds" },
        ["grant"] = new HashSet<string> { "action", "title", "per_action_max", "resources", "entity_states", "next_steps" },
        ["cap"] = new HashSet<string> { "quantity", "quantity_of", "max", "unit", "scale" },
        ["total"] = new HashSet<string> { "quantity", "quantity_of", "limit", "window", "unit", "scale", "measure", "per" },
        ["resources"] = new HashSet<string> { "parameter", "values" },
        ["quantity_of"] = new HashSet<string> { "kind", "parameter" },
    };

    /// <summary>
    /// Whether every key of each Card in KIFF's draft (<paramref name="draft"/>, as KIFF sent it) is one the check reads. A key
    /// whose value is null sets nothing. A Card's <c>id</c>, when sent, is the Card it revises. A <c>quantity_of</c> says what
    /// its <c>quantity</c> says: "sum(amount_cents)" is {kind: sum, parameter: amount_cents}, "count" is {kind: count}. The
    /// draft's own keys aren't a Card's. Anything else is refused, and nothing is signed.
    /// </summary>
    public static bool Known(JsonElement draft)
    {
        if (draft.ValueKind != JsonValueKind.Object) return false;
        return List(Get(draft, "cards"), card =>
        {
            if (!Only(card, "card")) return false;
            if (!Absent(Get(card, "id")))
            {
                var id = Get(card, "id");
                var revises = Get(card, "revises");
                if (id?.ValueKind != JsonValueKind.String || revises?.ValueKind != JsonValueKind.String
                    || id.Value.GetString() != revises.Value.GetString()) return false;
            }
            if (!Terms(card)) return false;
            var current = Get(card, "current");
            return Absent(current) || (Only(current!.Value, "current") && Terms(current.Value));
        });
    }

    static bool Terms(JsonElement t) => List(Get(t, "grants"), Grant) && List(Get(t, "aggregates"), Total);

    static bool Grant(JsonElement g)
    {
        if (!Only(g, "grant")) return false;
        var resources = Get(g, "resources");
        return List(Get(g, "per_action_max"), Cap) && (Absent(resources) || Only(resources!.Value, "resources"));
    }

    static bool Cap(JsonElement c) => Only(c, "cap") && QuantityOf(c);
    static bool Total(JsonElement t) => Only(t, "total") && QuantityOf(t);

    static bool QuantityOf(JsonElement o)
    {
        var of = Get(o, "quantity_of");
        if (Absent(of)) return true;
        var quantity = Get(o, "quantity");
        if (!Only(of!.Value, "quantity_of") || quantity?.ValueKind != JsonValueKind.String) return false;
        if (Get(of.Value, "kind") is not { ValueKind: JsonValueKind.String } kind) return false;
        var parameter = Get(of.Value, "parameter");
        if (!Absent(parameter) && parameter!.Value.ValueKind != JsonValueKind.String) return false;
        var said = Applied(quantity.Value.GetString()!);
        return kind.GetString() == said.Kind && (Absent(parameter) ? null : parameter!.Value.GetString()) == said.Parameter;
    }

    /// <summary>"sum(amount_cents)" as its kind and parameter; "count", or anything else, as itself.</summary>
    static (string Kind, string? Parameter) Applied(string quantity)
    {
        var open = quantity.IndexOf('(');
        if (open > 0 && quantity.EndsWith(')'))
        {
            var kind = quantity[..open];
            var parameter = quantity[(open + 1)..^1];
            if (kind.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') && parameter.Length > 0) return (kind, parameter);
        }
        return (quantity, null);
    }

    /// <summary>An object whose every key is the kind's, or null.</summary>
    static bool Only(JsonElement o, string kind) =>
        o.ValueKind == JsonValueKind.Object
        && o.EnumerateObject().All(p => p.Value.ValueKind == JsonValueKind.Null || Keys[kind].Contains(p.Name));

    /// <summary>Absent or null passes; else a list whose every item passes.</summary>
    static bool List(JsonElement? v, Func<JsonElement, bool> each) =>
        Absent(v) || (v!.Value.ValueKind == JsonValueKind.Array && v.Value.EnumerateArray().All(each));

    static bool Absent(JsonElement? v) => v is null || v.Value.ValueKind == JsonValueKind.Null;

    static JsonElement? Get(JsonElement o, string key) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(key, out var v) ? v : null;
}
