using System.Globalization;
using Tafs.Core.NeedsYou;
using Tafs.Core.Words;

namespace Tafs.Core.Record;

/// <summary>
/// What became of a call, as tafs cloud's Record says it (`done`, `approved`, `held`, `refused`, `not_done`,
/// `sending`, `unknown`) and KIFF's activity (`revoked`); and a taf's start, which only All shows.
/// Its dot: green done, approved or started; amber held, sending or unknown; red refused, not done or revoked.
/// </summary>
public enum EntryKind { Done, Approved, Held, Refused, NotDone, Sending, Unknown, Revoked, Started }

/// <summary>
/// One line of the Record: a taf's call, what KIFF decided and what the tool did. <paramref name="Amount"/> is
/// the money it moved, already written in the person's money; its words are in <paramref name="Detail"/>. <paramref name="Call"/> is
/// KIFF's call it's from, for its receipt; none for the sample's.
/// </summary>
public sealed record RecordEntry(string Taf, int TafColor, EntryKind Kind, string Title, string Detail, DateTimeOffset At, string? Amount = null,
    ActivityCall? Call = null);

/// <summary>tafs cloud's and KIFF's words for a kind.</summary>
public static class EntryKinds
{
    /// <summary>
    /// <c>done</c>, <c>approved</c>, <c>held</c>, <c>refused</c>, <c>not_done</c>, <c>sending</c>, <c>unknown</c>, <c>revoked</c>:
    /// anything else is Unknown, so a kind added later waits under Waiting rather than passing as done.
    /// </summary>
    public static EntryKind Parse(string? kind) => kind switch
    {
        "done" => EntryKind.Done,
        "approved" => EntryKind.Approved,
        "held" => EntryKind.Held,
        "refused" => EntryKind.Refused,
        "not_done" => EntryKind.NotDone,
        "sending" => EntryKind.Sending,
        "revoked" => EntryKind.Revoked,
        _ => EntryKind.Unknown,
    };
}

/// <summary>A line's dot's colour: green, amber or red.</summary>
public enum Dot { Went, Waiting, Stopped }

/// <summary>The Record's filters, as the iPad's.</summary>
public enum RecordFilter { All, MoneyMoved, Stopped, Waiting }

/// <summary>One day of the Record: its eyebrow ("TODAY · NOV 15") and its lines, newest first.</summary>
/// <param name="Reads">The day's reads of data, folded under one line on the Record (the iPad's <c>day.reads</c>); none when not folded.</param>
/// <param name="Day">The day, which a fold is kept open by.</param>
public sealed record RecordDay(string Title, IReadOnlyList<RecordEntry> Entries, IReadOnlyList<RecordEntry>? Reads = null, DateTime Day = default);

/// <summary>The Record (the iPad's RecordView): everything the tafs did, were refused, or asked, newest first.</summary>
public static class RecordScreen
{
    public static (string Title, string Lead) Heading(WordBook w) => (w.Get("record.title"), w.Get("record.lead"));

    public static string AllTafs(WordBook w) => w.Get("record.all_tafs");

    /// <summary>The tafs' chips after All tafs: each taf once, with its colour, the one started last first, as the iPad lists them.</summary>
    public static IReadOnlyList<(string Taf, int Colour)> TafChips(IEnumerable<RecordEntry> entries) =>
        entries.GroupBy(e => e.Taf).OrderByDescending(g => g.Min(e => e.At)).Select(g => (g.Key, g.First().TafColor)).ToList();

    public static IReadOnlyList<(RecordFilter Filter, string Title)> Filters(WordBook w) =>
    [
        (RecordFilter.All, w.Get("record.filter.all")),
        (RecordFilter.MoneyMoved, w.Get("record.filter.money_moved")),
        (RecordFilter.Stopped, w.Get("record.filter.stopped")),
        (RecordFilter.Waiting, w.Get("record.filter.waiting")),
    ];

    /// <summary>
    /// The iPad's rules (RecordFilter.keeps): Money moved, an amount that went (done or approved); Stopped, refused,
    /// not done or revoked; Waiting, held, sending or unknown. All keeps every line, a taf's start included.
    /// </summary>
    public static bool Keeps(RecordFilter filter, RecordEntry e) => filter switch
    {
        RecordFilter.MoneyMoved => e.Amount is not null && e.Kind is EntryKind.Done or EntryKind.Approved,
        RecordFilter.Stopped => e.Kind is EntryKind.Refused or EntryKind.NotDone or EntryKind.Revoked,
        RecordFilter.Waiting => e.Kind is EntryKind.Held or EntryKind.Sending or EntryKind.Unknown,
        _ => true,
    };

    /// <summary>A line's dot: what became of the call, at a glance.</summary>
    public static Dot DotOf(EntryKind kind) => kind switch
    {
        EntryKind.Held or EntryKind.Sending or EntryKind.Unknown => Dot.Waiting,
        EntryKind.Refused or EntryKind.NotDone or EntryKind.Revoked => Dot.Stopped,
        _ => Dot.Went,
    };

    /// <summary>The Record by day in <paramref name="zone"/>, newest first, narrowed to the filter and to one taf (or every taf).</summary>
    /// <param name="foldReads">
    /// The Record's way, as the iPad's RecordView: under All, a day's reads of data (KIFF's <c>read_only</c> calls) go apart, to
    /// fold under one line, so what moved shows first. Any other filter shows every call it keeps.
    /// </param>
    public static IReadOnlyList<RecordDay> Days(WordBook w, IEnumerable<RecordEntry> entries, RecordFilter filter, string? taf,
        DateTimeOffset now, TimeZoneInfo zone, bool foldReads = false)
    {
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        bool Read(RecordEntry e) => foldReads && filter == RecordFilter.All && e.Call?.ReadOnly == true;
        return entries.Where(e => Keeps(filter, e) && (taf is null || e.Taf == taf))
            .OrderByDescending(e => e.At)
            .GroupBy(e => TimeZoneInfo.ConvertTime(e.At, zone).Date)
            .Select(g => new RecordDay(foldReads && Spent(w, g) is { } spent ? $"{DayTitle(w, g.Key, today)} · {spent}" : DayTitle(w, g.Key, today),
                g.Where(e => !Read(e)).ToList(), g.Where(Read).ToList(), g.Key))
            .ToList();
    }

    /// <summary>
    /// The money a day moved, at the end of its heading on the Record, as the iPad's <c>spent</c>: "€640 spent", a sum for each
    /// currency joined with " + ", from KIFF's amounts on the calls that moved money; none when none did.
    /// </summary>
    public static string? Spent(WordBook w, IEnumerable<RecordEntry> entries)
    {
        var order = new List<string>();
        var sums = new Dictionary<string, (decimal Value, int Scale)>();
        foreach (var e in entries)
        {
            if (!Keeps(RecordFilter.MoneyMoved, e) || e.Call?.Amount is not { Unit: { Length: 3 } unit } amount
                || !decimal.TryParse(amount.Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
                continue;
            var scale = amount.Value.Split('.') is { Length: > 1 } parts ? parts[1].Length : 0;
            if (!sums.TryGetValue(unit, out var sum)) order.Add(unit);
            sums[unit] = (sum.Value + value, Math.Max(sum.Scale, scale));
        }
        var figures = order.Select(u => KiffActivity.AmountText(w, new CallAmount(sums[u].Value.ToString("F" + sums[u].Scale.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture), u))).OfType<string>().ToList();
        return figures.Count == 0 ? null : w.Get("record.day_spent", ("amount", string.Join(" + ", figures)));
    }

    /// <summary>The fold's line, as the iPad's: "· 11 reads of data, within its Card · Show", or Hide once open.</summary>
    public static string ReadsLine(WordBook w, int count, bool open) =>
        $"· {w.Count("record.reads", count)} · {w.Get(open ? "record.hide" : "record.show")}";

    /// <summary>"KIFF receipt" under a call whose receipt KIFF says is signed, as the iPad's RecordRow; none otherwise.</summary>
    public static string? ReceiptLine(WordBook w, RecordEntry e) => e.Call?.Receipt?.Status == "signed" ? w.Get("receipt.kiff_receipt") : null;

    /// <summary>"TODAY · NOV 15", "YESTERDAY · NOV 14", else "THURSDAY · NOV 12": in capitals, in the language's own way.</summary>
    public static string DayTitle(WordBook w, DateTime day, DateTime today)
    {
        var culture = Culture(w);
        var date = day.ToString(culture.DateTimeFormat.MonthDayPattern.Contains("MMMM")
            ? culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM") : culture.DateTimeFormat.MonthDayPattern, culture);
        var title = day == today ? w.Get("calendar.today_date", ("date", date))
            : day == today.AddDays(-1) ? w.Get("calendar.yesterday_date", ("date", date))
            : $"{day.ToString("dddd", culture)} · {date}";
        return title.ToUpper(culture);
    }

    /// <summary>With nothing to show: "Nothing yet." for the whole Record, "Nothing here." once narrowed.</summary>
    public static string Empty(WordBook w, RecordFilter filter, string? taf) =>
        w.Get(filter == RecordFilter.All && taf is null ? "common.nothing_yet" : "record.nothing_here");

    /// <summary>A line's title, "Chargebacks · Accept dispute on #3342", and its time, "00:17", 24-hour as the iPad shows it.</summary>
    public static (string Title, string Time) Line(RecordEntry e, TimeZoneInfo zone) =>
        ($"{e.Taf} · {e.Title}", TimeZoneInfo.ConvertTime(e.At, zone).ToString("HH:mm", CultureInfo.InvariantCulture));

    static CultureInfo Culture(WordBook w) => Tafs.Core.Words.Locale.Culture(w.Language);

    /// <summary>
    /// The iPad's sample Record (the reference set's record.png), in the person's language: what the five sample
    /// tafs did today, newest first (the scripts' second round, then their first), Deploys', Late parcels' and Support
    /// replies' calls held, and each taf started with its Card. No sample line moves money.
    /// </summary>
    public static IReadOnlyList<RecordEntry> Sample(WordBook w, DateTimeOffset now)
    {
        string S(string key) => w.Get("sample." + key);
        var alone = w.Get("record.within_card_alone");
        var you = w.Get("common.you");
        var held = NeedsYouScreen.Sample(w, now);
        var (leads, deploys, access, parcels, support) =
            (S("leads.name"), S("deploys.name"), S("access.name"), S("parcels.name"), S("support.name"));
        var colour = new Dictionary<string, int> { [leads] = 0, [deploys] = 1, [access] = 2, [parcels] = 3, [support] = 4 };

        var lines = new List<(string Taf, EntryKind Kind, string Title, string Detail, decimal? Euros)>
        {
            (leads, EntryKind.Done, S("leads.confirm_nord"), alone, null),
            (deploys, EntryKind.Held, w.Get("record.held", ("summary", held[0].Summary)), held[0].Reason, null),
            (access, EntryKind.Done, S("access.deactivate_tom"), alone, null),
            (parcels, EntryKind.Held, w.Get("record.held", ("summary", held[1].Summary)), held[1].Reason, null),
            (support, EntryKind.Held, w.Get("record.held", ("summary", held[2].Summary)), held[2].Reason, null),
            (leads, EntryKind.Done, S("leads.reply_kaya"), alone, null),
            (deploys, EntryKind.Done, S("deploys.deploy_api"), alone, null),
            (access, EntryKind.Done, S("access.sales_role"), alone, null),
            (parcels, EntryKind.Done, S("parcels.tell_5120"), alone, null),
            (support, EntryKind.Done, S("support.reply_7781"), alone, null),
        };
        foreach (var taf in new[] { support, parcels, access, deploys, leads })
            lines.Add((taf, EntryKind.Started, w.Get("record.started", ("taf", taf)), w.Get("record.card_issued_by", ("owner", you)), null));

        // A second apart, newest first, so the order holds whatever the clock.
        return lines.Select((l, i) => new RecordEntry(l.Taf, colour[l.Taf], l.Kind, l.Title, l.Detail, now.AddSeconds(-i),
            l.Euros is { } euros ? NeedsYouScreen.Euros(w, euros) : null)).ToList();
    }
}
