using System.Globalization;
using System.Text.Json;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

public enum HaltState { Running, Halting, Halted }

/// <summary>
/// A halt of one taf or, with none, of every taf, as tafs cloud answers it (the iPad's <c>TafsCloud.Halt</c>): running,
/// halting or halted, since when, and who holds it yet. kiff/agents starts no run, KIFF's gateway refuses its calls, and
/// planque sends nothing to the vendor. <see cref="Account"/>: halted because the whole account is.
/// </summary>
public sealed record Halt(HaltState State, DateTimeOffset? HaltedAt, bool Runtime, bool Kiff, bool Planque, bool Account, int? Stopped = null)
{
    /// <summary>Running, until read.</summary>
    public static readonly Halt Running = new(HaltState.Running, null, false, false, false, false);

    /// <summary>A taf's halt by its tafs cloud id, or every taf's with none.</summary>
    public static string Path(string? taf) => taf is null ? "/v1/halt" : $"/v1/tafs/{CloudRead.PathPart(taf)}/halt";

    /// <summary>
    /// <c>{"halt": {…}}</c>, as the iPad decodes it: its state one of the three, its four flags true or false, and
    /// <c>halted_at</c> and <c>stopped</c> absent or null when not given. None when it doesn't read.
    /// </summary>
    public static Halt? Read(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object || !answer.TryGetProperty("halt", out var h) || h.ValueKind != JsonValueKind.Object) return null;
        HaltState? state = Lenient.Text(h, "state") switch
        {
            "running" => HaltState.Running,
            "halting" => HaltState.Halting,
            "halted" => HaltState.Halted,
            _ => null,
        };
        bool? Flag(string name) => h.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
        if (state is not { } s || Flag("runtime") is not { } runtime || Flag("kiff") is not { } kiff || Flag("planque") is not { } planque
            || Flag("account") is not { } account) return null;
        DateTimeOffset? at = null;
        if (h.TryGetProperty("halted_at", out var t) && t.ValueKind != JsonValueKind.Null)
        {
            if (Lenient.At(h, "halted_at") is not { } when) return null;
            at = when;
        }
        int? stopped = null;
        if (h.TryGetProperty("stopped", out var n) && n.ValueKind != JsonValueKind.Null)
        {
            if (!n.TryGetInt32(out var count)) return null;
            stopped = count;
        }
        return new(s, at, runtime, kiff, planque, account, stopped);
    }

    /// <summary>The screen's headline (the iPad's <c>HaltScreen.headline</c>): Running, Halting… or Halted.</summary>
    public static string Headline(WordBook w, Halt h) => w.Get(h.State switch
    {
        HaltState.Running => "taf.now.running",
        HaltState.Halting => "taf.halt.halting",
        _ => "taf.state.halted",
    });

    /// <summary>
    /// What halting does, or where it stands (the iPad's <c>HaltScreen.explanation</c>): since when it holds, in this PC's
    /// time, as every screen here shows it.
    /// </summary>
    public static string Explanation(WordBook w, Halt h, bool all, TimeZoneInfo zone) => h.State switch
    {
        HaltState.Running => w.Get("halt.halting_stops_no_run_starts", ("who", w.Get(all ? "halt.your_tafs_lower" : "record.on.it"))),
        HaltState.Halting => w.Get("halt.explain.halting"),
        _ => h.HaltedAt is { } at
            ? w.Get("halt.explain.halted_since", ("time", TimeZoneInfo.ConvertTime(at, zone).ToString("HH:mm", CultureInfo.InvariantCulture)))
            : w.Get("halt.explain.halted"),
    };

    /// <summary>A halt row's second line (the iPad's <c>HaltRow</c>): "Stop it until you resume" while running, else the headline.</summary>
    public static string RowLine(WordBook w, Halt h) => h.State == HaltState.Running ? w.Get("halt.row_running") : Headline(w, h);

    /// <summary>
    /// Whether Home offers every taf's halt (the iPad's <c>showsAccountHalt</c>): to someone who manages Cards, while a taf
    /// runs, and whenever the account is halted or halting, tafs or not, since that row is where it's resumed from.
    /// </summary>
    public static bool ShowsAccountHalt(bool managesCards, bool anyTaf, Halt account) => managesCards && (anyTaf || account.State != HaltState.Running);

    /// <summary>Whether the person manages the account's Cards (the iPad's <c>managesCards</c>): <c>cards.manage</c>, or when KIFF doesn't say.</summary>
    public static bool ManagesCards(IReadOnlyList<string>? capabilities) => capabilities?.Contains("cards.manage") ?? true;

    /// <summary>
    /// Whether a taf's page has its Halt row: not for a revoked taf, as the iPad's <c>taf.state != .revoked</c>
    /// (TafDetailView.swift), which is every state tafs cloud sends that it doesn't know, <c>"stopped"</c> among them.
    /// </summary>
    public static bool HasRow(Home.HomeTaf taf) => taf.StateKey != "stopped";
}
