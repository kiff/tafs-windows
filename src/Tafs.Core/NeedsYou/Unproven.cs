using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.NeedsYou;

/// <summary>
/// A money call whose outcome KIFF can't prove, as the iPad's <c>KIFFClient.Unproven</c>: its answer was lost, or the vendor
/// refused it with nothing proving no effect, or it's one of the first outcomes of a money operation on probation. The
/// person says whether it happened.
/// </summary>
/// <param name="Status">unknown, refused_by_tool, or probation.</param>
/// <param name="Amount">In minor units of <paramref name="Unit"/>, at <paramref name="Scale"/>.</param>
/// <param name="Words">KIFF's sentence for the call ("Refund a payment: 40.00 EUR."), shown as it is.</param>
/// <param name="StatusWords">Why KIFF asks, in its words.</param>
public sealed record Unproven(string Proposal, string Title, string Status, string? Agent = null, string? Tool = null, long? Amount = null,
    string? Unit = null, int? Scale = null, string? At = null, string? Words = null, string? StatusWords = null)
{
    /// <summary>The amount as the iPad's <c>money</c>: two places unless KIFF says, and none for an amount with no unit.</summary>
    public KiffAmount? Money => Amount is { } minor ? new KiffAmount(minor, Unit, Unit is null ? 0 : Scale ?? 2) : null;
}

/// <summary>"Did this happen?" (the iPad's <c>didThisHappen</c>, <c>loadUnproven</c> and <c>DidThisHappenCard</c>).</summary>
public static class DidThisHappen
{
    public const string Path = "/v1/me/did-this-happen";

    /// <summary>KIFF's <c>holds.answer</c>, as the iPad's <c>mayAnswerHolds</c>: an account that lists no capabilities may.</summary>
    public static bool MayAnswerHolds(IReadOnlyList<string>? capabilities) => capabilities?.Contains("holds.answer") ?? true;

    /// <summary>
    /// <c>{"calls": […]}</c>, newest first as KIFF sends them, as the iPad's decoder: each call's proposal is required, its
    /// title "" and its status unknown when not said, every other field checked for its kind when it's there. Anything else
    /// fails the read. No calls is none.
    /// </summary>
    public static IReadOnlyList<Unproven>? Read(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object) return null;
        if (!answer.TryGetProperty("calls", out var calls) || calls.ValueKind == JsonValueKind.Null) return [];
        if (calls.ValueKind != JsonValueKind.Array) return null;
        var list = new List<Unproven>();
        foreach (var c in calls.EnumerateArray())
        {
            if (c.ValueKind != JsonValueKind.Object || !Text(c, "proposal", out var proposal) || proposal is null
                || !Text(c, "agent", out var agent) || !Text(c, "tool", out var tool) || !Text(c, "title", out var title)
                || !Text(c, "status", out var status) || !Whole(c, "amount", out var amount) || !Text(c, "unit", out var unit)
                || !Whole(c, "scale", out var scale) || scale is < int.MinValue or > int.MaxValue || !Text(c, "at", out var at)
                || !Text(c, "words", out var words) || !Text(c, "status_words", out var statusWords))
                return null;
            list.Add(new(proposal, title ?? "", status ?? "unknown", agent, tool, amount, unit, (int?)scale, at, words, statusWords));
        }
        return list;
    }

    /// <summary>
    /// The card's lines, as <c>DidThisHappenCard</c>: its title; KIFF's words for the call, else its title; the amount only
    /// without KIFF's words; why KIFF asks, in its words.
    /// </summary>
    public static (string Title, string Call, string? Amount, string? Why) Card(WordBook w, Unproven call) =>
        (w.Get("record.did_this_happen.title"), call.Words ?? call.Title, call.Words is null ? call.Money?.Formatted(w) : null, call.StatusWords);

    /// <summary>A string, or absent or null; anything else fails the read, as Swift's <c>decodeIfPresent</c>.</summary>
    static bool Text(JsonElement e, string name, out string? value)
    {
        value = null;
        if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return true;
        if (v.ValueKind != JsonValueKind.String) return false;
        value = v.GetString();
        return true;
    }

    /// <summary>A whole number, or absent or null, as Swift decodes an <c>Int64</c>: 40.0 is 40, 40.5 fails the read.</summary>
    static bool Whole(JsonElement e, string name, out long? value)
    {
        value = null;
        if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return true;
        if (v.ValueKind != JsonValueKind.Number) return false;
        if (v.TryGetInt64(out var n)) { value = n; return true; }
        if (v.TryGetDecimal(out var d) && d == decimal.Truncate(d) && d is >= long.MinValue and <= long.MaxValue) { value = (long)d; return true; }
        return false;
    }
}
