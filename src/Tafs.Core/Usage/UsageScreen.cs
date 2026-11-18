using System.Text.Json;
using Tafs.Core.Words;

namespace Tafs.Core.Usage;

/// <summary>tafs cloud's tone for a line: ok (calm), warning (amber, from 80%) or waiting (red: no new run starts).</summary>
public enum Tone { Ok, Warning, Waiting }

/// <summary>
/// The balance now (GET /v1/usage/balance), in tafs cloud's words: a bar at <see cref="MeterPct"/> where something bounds it.
/// <see cref="Raise"/> is Raise the limit's words, set only for someone who may raise it.
/// </summary>
public sealed record Balance(Tone State, string Words, int? MeterPct, string? Detail, string? Warning, string? Raise,
    bool CreditsPanel, string? PlanLine, Tone PlanTone, string? HomeLine);

/// <summary>One taf's part of a period. A row with no <see cref="Taf"/> is the tafs removed since.</summary>
public sealed record CreditsTaf(string? Taf, int Credits, int Runs, string Words);

/// <summary>Runs not counted, and each reason (`coach`, `trial`, `failed`) in tafs cloud's words.</summary>
public sealed record NotCounted(int Runs, string Words, IReadOnlyList<(string Why, int Runs, string Words)> Lines);

/// <summary>
/// The credits the tafs used in a period (GET /v1/usage/credits): in all, by taf, Coach's paid use past its fair use
/// (counted, its own row), what wasn't counted and why, and a note. Every line is tafs cloud's words.
/// </summary>
public sealed record Credits(int Total, int Runs, string Words, IReadOnlyList<CreditsTaf> Tafs, string? Coach, NotCounted? NotCounted,
    string? Note);

/// <summary>Plan &amp; usage, as on the iPad (its UsageView): the balance, then the Credits panel. The app adds nothing to tafs cloud's words but their order.</summary>
public static class UsageScreen
{
    public static Tone ToneOf(string? word) => word switch { "warning" => Tone.Warning, "waiting" => Tone.Waiting, _ => Tone.Ok };

    /// <summary>The balance, leniently: a field it can't read is left out, never the read; no words, no balance.</summary>
    public static Balance? ReadBalance(string json)
    {
        var root = JsonDocument.Parse(json).RootElement;
        if (!root.TryGetProperty("balance", out var b) || b.ValueKind != JsonValueKind.Object || Text(b, "words") is not { } words) return null;
        int? pct = b.TryGetProperty("meter_pct", out var m) && m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out var n) ? Math.Clamp(n, 0, 100) : null;
        var raise = b.TryGetProperty("raise", out var r) && r.ValueKind == JsonValueKind.Object ? Text(r, "words") : null;
        var panel = !(b.TryGetProperty("credits_panel", out var p) && p.ValueKind == JsonValueKind.False);
        return new Balance(ToneOf(Text(b, "state")), words, pct, Text(b, "detail"), Text(b, "warning"), raise, panel,
            Text(root, "plan_line"), ToneOf(Text(root, "plan_tone")), Text(root, "home_line"));
    }

    /// <summary>A period's credits, leniently: a line it can't read is left out, not the read.</summary>
    public static Credits? ReadCredits(string json)
    {
        var root = JsonDocument.Parse(json).RootElement;
        if (Text(root, "words") is not { } words || !root.TryGetProperty("credits", out var c) || c.ValueKind != JsonValueKind.Number || !c.TryGetInt32(out var total)) return null;
        var tafs = Items(root, "tafs").Select(t => Text(t, "words") is { } w ? new CreditsTaf(Text(t, "taf"), Int(t, "credits"), Int(t, "runs"), w) : null)
            .OfType<CreditsTaf>().ToList();
        NotCounted? not = null;
        if (root.TryGetProperty("not_counted", out var nc) && nc.ValueKind == JsonValueKind.Object)
            not = new NotCounted(Int(nc, "runs"), Text(nc, "words") ?? "",
                Items(nc, "lines").Where(l => Text(l, "why") is not null && Text(l, "words") is not null)
                    .Select(l => (Text(l, "why")!, Int(l, "runs"), Text(l, "words")!)).ToList());
        var coach = root.TryGetProperty("coach", out var co) && co.ValueKind == JsonValueKind.Object ? Text(co, "words") : null;
        return new Credits(total, Int(root, "runs"), words, tafs, coach, not, Text(root, "note"));
    }

    static IEnumerable<JsonElement> Items(JsonElement o, string name) =>
        o.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object) : [];

    static string? Text(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    static int Int(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

    /// <summary>The not-counted panel shows when it has its words or a reason.</summary>
    public static bool ShowsNotCounted(Credits c) => c.NotCounted is { } n && (n.Words.Length > 0 || n.Lines.Count > 0);

    /// <summary>The iPad's sample, the same as Android's: Solo, about half the period's credits left.</summary>
    public static Balance SampleBalance(WordBook w) => new(Tone.Ok, w.Get("sample.credits.balance_words"), 48,
        w.Get("sample.credits.balance_detail"), null, null, true, w.Get("sample.credits.plan_line"), Tone.Ok, null);

    /// <summary>The sample's credits, this period or the one before, in its tafs' words as tafs cloud writes them.</summary>
    public static Credits SampleCredits(WordBook w, bool previous = false) => previous
        ? new(2310, 126, w.Get("sample.credits.last_period"),
            [new("sample-support", 1480, 74, w.Get("sample.credits.refunds_last")), new("sample-parcels", 830, 52, w.Get("sample.credits.invoices_last"))],
            null, null, w.Get("sample.credits.note"))
        : new(1240, 69, w.Get("sample.credits.this_period"),
            [new("sample-support", 820, 41, w.Get("sample.credits.refunds_this")), new("sample-parcels", 420, 28, w.Get("sample.credits.invoices_this"))],
            null,
            new NotCounted(9, w.Get("sample.credits.not_counted"),
                [("coach", 6, w.Get("sample.credits.not_counted_coach")), ("trial", 2, w.Get("sample.credits.not_counted_trials")),
                 ("failed", 1, w.Get("sample.credits.not_counted_failed"))]),
            w.Get("sample.credits.note"));
}
