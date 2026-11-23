using System.Globalization;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>What happens to a call past a Card's limits, as KIFF says it (<c>outside: "hold" | "refuse"</c>).</summary>
public enum Outside { Hold, Refuse }

/// <summary>
/// One action a Card allows, and its limits (the iPad's CardRule): up to an amount each, a total for its window, a count
/// of calls. Past them a call is held for the person or refused. Never: never allowed. <paramref name="PerWindow"/> is
/// KIFF's, at the ceiling it has now; with extra room, the room, the Card's own total and when the room ends come apart.
/// </summary>
public sealed record CardRule(string Action, string Label, CardWindow Window, KiffAmount? PerAction = null,
    KiffAmount? PerWindow = null, int? CountPerWindow = null, bool Never = false, bool AlwaysAsk = false, Outside Outside = Outside.Hold,
    KiffAmount? ExtraRoom = null, KiffAmount? BaseLimit = null, DateTimeOffset? RoomEnds = null)
{
    /// <summary>
    /// The Card's own total for its window: KIFF's, without the extra room in it. With room but no own limit from KIFF,
    /// it's unknown: <c>per_window</c> has the room folded in, so it's never shown as the Card's own.
    /// </summary>
    public KiffAmount? OwnTotal => ExtraRoom is null ? PerWindow : BaseLimit;

    /// <summary>"+€30 extra room today", or "+€30 extra room until 00:00", when KIFF says there's some.</summary>
    public string? RoomWords(WordBook w, TimeZoneInfo? zone = null, DateTimeOffset? now = null) =>
        ExtraRoom is not { } extra || BaseLimit is null ? null
        : RoomEnds is { } ends ? w.Get("card.extra_room_until", ("extra", extra.Formatted(w)), ("ends", TafCard.EndsWords(w, ends, zone, now)))
        : w.Get("card.extra_room", ("extra", extra.Formatted(w)), ("window", Window.Current));

    /// <summary>
    /// Its limits in the words KIFF's Cards use, the same on every app: "up to €150 each; more asks you first · €2,000 in
    /// total a day, then it asks you" when past them it asks; "≤ €150 each · €2,000 a day" when it refuses.
    /// </summary>
    public string Summary(WordBook w, TimeZoneInfo? zone = null, DateTimeOffset? now = null)
    {
        if (Never) return w.Get("card.rule.never");
        if (AlwaysAsk) return w.Get("card.rule.always_asks");
        var parts = new List<string>();
        var room = RoomWords(w, zone, now);
        if (Outside == Outside.Hold)
        {
            if (PerAction is { } each) parts.Add(w.Get("card.rule.up_to_each", ("amount", each.Formatted(w))));
            if (OwnTotal is { } total) parts.Add(w.Get("card.asks_you", ("formatted", total.Formatted(w)), ("sum_label", Window.SumLabel(w))));
            if (room is not null) parts.Add(room);
            if (CountPerWindow is { } count)
                parts.Add(count == 0 ? w.Get("card.held.each_call_asks") : w.Count("card.rule.calls_then_asks", count, ("window", Window.Label)));
            return string.Join(" · ", parts);
        }
        if (PerAction is { } most) parts.Add(w.Get("card.rule.at_most_each", ("amount", most.Formatted(w))));
        if (OwnTotal is { } own) parts.Add($"{own.Formatted(w)} {(own.Unit is null ? Window.SumLabel(w) : Window.Label)}");
        if (room is not null) parts.Add(room);
        if (CountPerWindow is { } calls) parts.Add(w.Count("card.calls_window", calls, ("window", Window.Label)));
        return string.Join(" · ", parts);
    }

    /// <summary>Under its name: what happens past its limits, "Outside: held for you" or "Outside: refused".</summary>
    public string OutsideWords(WordBook w) => w.Get(Outside == Outside.Hold ? "card.outside_held" : "card.outside_refused");
}

/// <summary>
/// How KIFF counts one tool the Card covers, as the owner approved it (the iPad's <c>CardCounted</c>): the tool, its title,
/// and KIFF's lines, word for word.
/// </summary>
public sealed record CardCounted(string Tool, string Title, IReadOnlyList<string> Lines);

/// <summary>
/// One KIFF Card (the iPad's TafCard): a taf can hold several, each with its own limits and totals, and a Card scoped to a
/// test environment is kept apart from the live one. Its money total and its count of calls are KIFF's, never worked out here.
/// How this is counted (<see cref="Counted"/>) and its totals for each item apart (<see cref="EachItem"/>) are KIFF's and tafs
/// cloud's words; none when they send none.
/// </summary>
public sealed record TafCard(string Id, string Title, IReadOnlyList<CardRule> Rules, CardTotal? Total = null, CallsTotal? Calls = null,
    string Environment = "", bool Live = true, bool Active = true, DateTimeOffset? Expires = null,
    IReadOnlyList<CardCounted>? Counted = null, IReadOnlyList<string>? EachItem = null)
{
    /// <summary>The pill on its face when it's scoped to an environment that isn't production ("TEST"); none for a live Card.</summary>
    public string? EnvironmentLabel => Live || Environment.Length == 0 ? null : Environment.ToUpperInvariant();

    /// <summary>
    /// What a vendor's refusal still counts against its totals, in KIFF's words (the iPad's <c>pending</c>: its money total's
    /// then its calls'), for a live, active Card only (<c>countsForReal</c>): a test Card's totals aren't what the taf may do.
    /// </summary>
    public IReadOnlyList<string> PendingLines => Live && Active
        ? new[] { Total?.Pending, Calls?.Pending }.Select(p => p?.Trim()).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList()
        : [];

    /// <summary>Why its money total has no currency, in KIFF's words (a tool it grants isn't connected any more); none when it has one.</summary>
    public string? UnitWords => Total?.UnitWords?.Trim() is { Length: > 0 } words ? words : null;

    /// <summary>
    /// Its face's big figure and the words under it, as the iPad's TafCard.hero: what's left of its money total, else of its
    /// calls. What's left only when the use is known in the limit's own unit and scale; otherwise the limit alone, its use
    /// unavailable, never zero left or used. A bare number says so once, after the words.
    /// </summary>
    public (string Figure, string Words)? Hero(WordBook w, TimeZoneInfo? zone = null, DateTimeOffset? now = null)
    {
        // With room but no own limit from KIFF, its limit has the room folded in: no money figure, never a summed one.
        if (Total is { } t && (t.Extra is null || t.Base is not null))
        {
            var note = t.Limit.Unit is null ? w.Get("card.hero.no_currency_suffix") : "";
            if (t.Used is { } used && used.SameKind(t.Limit))
            {
                var left = new KiffAmount(Math.Max(0, t.Limit.Minor - used.Minor), t.Limit.Unit, t.Limit.Scale);
                // The Card's own total and the extra room apart, as KIFF says them: "of €300 + €30 extra room until 00:00".
                if (t.Extra is { } extra && t.Base is { } own)
                {
                    var until = t.RoomEnds is { } ends ? w.Get("card.until_ends", ("ends", EndsWords(w, ends, zone, now))) : "";
                    return (left.Figure(w), w.Get("card.left_extra_room", ("current", t.Window.Current), ("own", own.Figure(w)),
                        ("extra", extra.Figure(w)), ("until", until), ("note", note)));
                }
                return (left.Figure(w), w.Get("card.hero.left_of_note", ("window", t.Window.Current), ("limit", t.Limit.Figure(w)), ("note", note)));
            }
            return (t.Limit.Figure(w), w.Get("card.hero.use_unavailable_note", ("label", t.Window.Label), ("note", note)));
        }
        if (Calls is { } c)
        {
            // Written as the iPad writes a count: plain, ungrouped.
            var limit = c.Limit.ToString(CultureInfo.InvariantCulture);
            if (c.Used is not { } made) return (limit, w.Get("card.hero.calls_use_unavailable", ("window", c.Window.Label)));
            var left = Math.Max(0, c.Limit - made);
            return (left.ToString(CultureInfo.InvariantCulture), w.Count("card.hero.calls_left_of", left, ("window", c.Window.Current), ("limit", limit)));
        }
        return null;
    }

    /// <summary>
    /// The lines under the figure: with one, its main rule's "up to €150 each; more asks you first" and "2 tools"; with
    /// none, its first two rules in short and how many more.
    /// </summary>
    public IReadOnlyList<string> Facts(WordBook w)
    {
        if (Hero(w) is null)
        {
            var lines = Rules.Take(2).Select(r => w.Get("card.rule_line", ("rule", r.Label.ToLower(Culture(w))), ("summary", r.Summary(w)))).ToList();
            if (Rules.Count > 2) lines.Add(w.Count("card.more", Rules.Count - 2));
            return lines;
        }
        var facts = new List<string>();
        if (Rules.FirstOrDefault(r => r.PerAction is not null) is { PerAction: { } each } rule)
            facts.Add(w.Get(rule.Outside == Outside.Hold ? "card.rule.up_to_each" : "card.rule.at_most_each_refused_above", ("amount", each.Formatted(w))));
        if (Rules.Count > 1) facts.Add(w.Count("card.tools", Rules.Count));
        return facts;
    }

    /// <summary>
    /// The Card bar above the Activity: its figure and words, else its name, and under it its name, its environment when it
    /// isn't production, and how many Cards the taf holds ("Refunds · 3 Cards").
    /// </summary>
    public CardBarLine Bar(WordBook w, int cards)
    {
        var hero = Hero(w);
        var under = new List<string> { Title };
        if (EnvironmentLabel is not null) under.Add(Culture(w).TextInfo.ToTitleCase(Environment.ToLowerInvariant()));
        if (cards > 1) under.Add(w.Count("card.count", cards));
        return new(hero is { } h ? $"{h.Figure} {h.Words}" : Title, string.Join(" · ", under));
    }

    /// <summary>
    /// The Card as Narrator reads it, as the iPad's TafCardFace.spoken: what's left first, then its name and environment,
    /// its facts ("No actions yet" with none), who issued it, and its end: Revoked; Ended, on its date when it has one; Until
    /// its date; or No end date.
    /// </summary>
    public string Spoken(WordBook w, string issuedBy, bool revoked)
    {
        var parts = new List<string>();
        if (Hero(w) is { } hero) parts.Add($"{hero.Figure} {hero.Words}");
        parts.Add(string.Join(", ", new[] { Title, EnvironmentLabel }.OfType<string>()));
        parts.AddRange(Facts(w));
        if (Rules.Count == 0) parts.Add(w.Get("card.no_actions"));
        parts.Add(w.Get("card.issued_by_named", ("who", issuedBy)));
        var end = Expires is { } at ? at.ToString("d MMM yyyy", Culture(w)) : null;
        parts.Add(revoked ? w.Get("card.revoked")
            : !Active ? end is null ? w.Get("card.ended") : w.Get("card.ended_on", ("date", end))
            : end is null ? w.Get("card.no_end_date") : w.Get("card.until_date", ("date", end)));
        return string.Join(". ", parts.Select(p => p.Trim())) + ".";
    }

    /// <summary>When something ends, as the iPad's endsWords: the time alone today, its date too on a later day.</summary>
    public static string EndsWords(WordBook w, DateTimeOffset at, TimeZoneInfo? zone = null, DateTimeOffset? now = null)
    {
        zone ??= TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(at, zone);
        var today = TimeZoneInfo.ConvertTime(now ?? DateTimeOffset.UtcNow, zone).Date;
        return local.Date == today ? local.ToString("HH:mm", CultureInfo.InvariantCulture) : local.ToString("d MMM yyyy, HH:mm", Culture(w));
    }

    static CultureInfo Culture(WordBook w) => Tafs.Core.Words.Locale.Culture(w.Language);
}
