using System.Globalization;
using Tafs.Core.NeedsYou;
using Tafs.Core.Words;

namespace Tafs.Core.Record;

/// <summary>What became of a call: its dot's colour on the Record.</summary>
public enum EntryKind { Done, Held, Refused, Started }

/// <summary>
/// One line of the Record: a taf's call, what KIFF decided and what the tool did. <paramref name="Amount"/> is
/// the money it moved, already written in the person's money; its words are in <paramref name="Detail"/>.
/// </summary>
public sealed record RecordEntry(string Taf, int TafColor, EntryKind Kind, string Title, string Detail, DateTimeOffset At, string? Amount = null);

/// <summary>The Record's filters, as the iPad's (kiff/tafs#167).</summary>
public enum RecordFilter { All, MoneyMoved, Stopped, Waiting }

/// <summary>One day of the Record: its eyebrow ("TODAY · NOV 15") and its lines, newest first.</summary>
public sealed record RecordDay(string Title, IReadOnlyList<RecordEntry> Entries);

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

    /// <summary>Money moved: done, with an amount. Stopped: refused. Waiting: held.</summary>
    public static bool Keeps(RecordFilter filter, RecordEntry e) => filter switch
    {
        RecordFilter.MoneyMoved => e.Amount is not null && e.Kind == EntryKind.Done,
        RecordFilter.Stopped => e.Kind == EntryKind.Refused,
        RecordFilter.Waiting => e.Kind == EntryKind.Held,
        _ => true,
    };

    /// <summary>The Record by day in <paramref name="zone"/>, newest first, narrowed to the filter and to one taf (or every taf).</summary>
    public static IReadOnlyList<RecordDay> Days(WordBook w, IEnumerable<RecordEntry> entries, RecordFilter filter, string? taf,
        DateTimeOffset now, TimeZoneInfo zone)
    {
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        return entries.Where(e => Keeps(filter, e) && (taf is null || e.Taf == taf))
            .OrderByDescending(e => e.At)
            .GroupBy(e => TimeZoneInfo.ConvertTime(e.At, zone).Date)
            .Select(g => new RecordDay(DayTitle(w, g.Key, today), g.ToList()))
            .ToList();
    }

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

    static CultureInfo Culture(WordBook w) => CultureInfo.GetCultureInfo(w.Language == "pt" ? "pt-PT" : w.Language);

    /// <summary>
    /// The iPad's sample Record (the reference set's record.png), in the person's language: what the five sample
    /// tafs did today, newest first, Deploys' and Bill pay's calls held, and each taf started with its Card.
    /// </summary>
    public static IReadOnlyList<RecordEntry> Sample(WordBook w, DateTimeOffset now)
    {
        string S(string key) => w.Get("sample." + key);
        string Within(decimal euros) => w.Get("record.within_card", ("amount", NeedsYouScreen.Euros(w, euros)));
        var alone = w.Get("record.within_card_alone");
        var you = w.Get("common.you");
        var held = NeedsYouScreen.Sample(w, now);
        var (chargebacks, deploys, access, billPay, refunds) =
            (S("chargebacks.name"), S("deploys.name"), S("access.name"), S("bill_pay.name"), S("refunds.name"));
        var colour = new Dictionary<string, int> { [chargebacks] = 0, [deploys] = 1, [access] = 2, [billPay] = 3, [refunds] = 4 };

        var lines = new List<(string Taf, EntryKind Kind, string Title, string Detail, decimal? Euros)>
        {
            (chargebacks, EntryKind.Done, S("chargebacks.accept_3342"), Within(35), 35),
            (deploys, EntryKind.Held, w.Get("record.held", ("summary", held[0].Summary)), held[0].Reason, null),
            (access, EntryKind.Done, S("access.deactivate_tom"), alone, null),
            (billPay, EntryKind.Held, w.Get("record.held", ("summary", held[1].Summary)), held[1].Reason, null),
            (refunds, EntryKind.Done, S("refunds.credit_2290"), Within(25), 25),
            (chargebacks, EntryKind.Done, S("chargebacks.answer_3318"), Within(120), 120),
            (deploys, EntryKind.Done, S("deploys.deploy_api"), alone, null),
            (access, EntryKind.Done, S("access.sales_role"), alone, null),
            (billPay, EntryKind.Done, S("bill_pay.pay_northwind"), Within(1_240), 1_240),
            (refunds, EntryKind.Done, S("refunds.refund_4471"), Within(40), 40),
        };
        foreach (var taf in new[] { refunds, billPay, access, deploys, chargebacks })
            lines.Add((taf, EntryKind.Started, w.Get("record.started", ("taf", taf)), w.Get("record.card_issued_by", ("owner", you)), null));

        // A second apart, newest first, so the order holds whatever the clock.
        return lines.Select((l, i) => new RecordEntry(l.Taf, colour[l.Taf], l.Kind, l.Title, l.Detail, now.AddSeconds(-i),
            l.Euros is { } euros ? NeedsYouScreen.Euros(w, euros) : null)).ToList();
    }
}
