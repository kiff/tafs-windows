using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tafs.Core.NeedsYou;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Record;

/// <summary>
/// What a Record line acted on, and whether a held one still waits (the iPad's <c>RecordEntry.onText</c>,
/// <c>KIFFMapping.naming</c>, <c>nameRecordLines</c> and <c>isWaiting</c>, kiff/tafs#167).
/// </summary>
public static class RecordNaming
{
    /// <summary>
    /// What a taf's calls through KIFF acted on (<c>GET /v1/tafs/{id}/calls</c>): KIFF's key for the call and the value of the
    /// argument its Card scopes on, kept 30 days.
    /// </summary>
    public static string CallsPath(string taf) => "/v1/tafs/" + CloudRead.PathPart(taf) + "/calls";

    /// <summary>tafs cloud's call refs, by KIFF's key (<c>invocation</c>); one without both is left out. None for an answer that isn't a list.</summary>
    public static IReadOnlyDictionary<string, string>? Refs(JsonElement answer)
    {
        if (Lenient.Field(answer, "calls") is not { ValueKind: JsonValueKind.Array } list) return null;
        var refs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in list.EnumerateArray())
            if (r.ValueKind == JsonValueKind.Object && Lenient.Text(r, "invocation") is { Length: > 0 } key && Lenient.Text(r, "value") is { } value)
                refs[key] = value;
        return refs;
    }

    /// <summary>Whether a value is what KIFF's hash names: <c>sha256:</c> and the lowercase hex of its UTF-8 bytes, untrimmed, in its own case.</summary>
    public static bool IsOn(string value, string hash) =>
        hash == "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>The lines that name something KIFF hashed and tafs doesn't name yet: their tafs' calls are read.</summary>
    public static bool Unnamed(RecordEntry e) => e.Call is { OnHash: not null, EntityId: not null } && e.OnValue is null;

    /// <summary>
    /// The lines with what each acted on, from tafs cloud's refs by KIFF's key: only a value whose hash is KIFF's, so a line
    /// never names a payment KIFF didn't.
    /// </summary>
    public static IReadOnlyList<RecordEntry> Named(IReadOnlyList<RecordEntry> lines, IReadOnlyDictionary<string, string> refs) =>
        [.. lines.Select(e => e.Call is { EntityId: { } key, OnHash: { } hash } && e.OnValue is null && refs.TryGetValue(key, out var value) && IsOn(value, hash)
            ? e with { OnValue = value } : e)];

    /// <summary>"payment …QbstR": what it acted on, by the argument's name ("it" without one), its value's last five past six.</summary>
    public static string? OnText(WordBook w, RecordEntry e)
    {
        if (e.OnValue is not { } value) return null;
        var noun = (e.Call?.OnArg ?? w.Get("record.on.it")).Replace('_', ' ');
        return value.Length > 6 ? w.Get("record.on.short", ("noun", noun), ("end", value[^5..])) : w.Get("record.on.whole", ("noun", noun), ("value", value));
    }

    /// <summary>
    /// Whether a held line still waits, as the iPad's <c>isWaiting</c>: KIFF still holds its call (by the call's
    /// <c>proposal_id</c>, else the hold's id). Before Needs you is read (<paramref name="held"/> none), it does.
    /// </summary>
    public static bool StillWaiting(RecordEntry e, IReadOnlyList<HeldCall>? held) =>
        held is null || e.Call?.ProposalId is not { } id || held.Any(h => (h.ProposalId ?? h.KiffId) == id);

    /// <summary>
    /// A line's words under its title, as the iPad's <c>RecordRow.detail</c>: what it acted on first; then, for a held call KIFF
    /// no longer holds (it expired, or was withdrawn when its run stopped), "no longer waiting · nothing sent"; else its own.
    /// </summary>
    public static string Detail(WordBook w, RecordEntry e, IReadOnlyList<HeldCall>? held)
    {
        var parts = new List<string>();
        if (OnText(w, e) is { } on) parts.Add(on);
        if (e.Kind == EntryKind.Held && !StillWaiting(e, held))
        {
            parts.Add(w.Get("record.no_longer_waiting"));
            parts.Add(w.Get("record.nothing_sent"));
        }
        else if (e.Detail.Length > 0) parts.Add(e.Detail);
        return string.Join(" · ", parts);
    }
}
