using Tafs.Core.Record;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Record;

[Part("record")]
public class RecordScreenTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 15, 0, 17, 30, TimeSpan.Zero);
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    public static IEnumerable<object[]> Shipped() => WordBook.Shipped(SharedFiles.Strings).Select(l => new object[] { l });

    static RecordEntry Entry(EntryKind kind, string? amount = null, string taf = "Refunds", int hoursAgo = 0) =>
        new(taf, 0, kind, "Refund order #4471", "", Now.AddHours(-hoursAgo), amount);

    [Fact]
    public void The_filters_are_the_iPad_s() =>
        Assert.Equal(new[] { "All", "Money moved", "Stopped", "Waiting" }, RecordScreen.Filters(En).Select(f => f.Title));

    /// <summary>One line of each kind, with an amount, against each filter: the iPad's RecordFilter.keeps.</summary>
    [Theory]
    [InlineData(EntryKind.Done, true, false, false)]
    [InlineData(EntryKind.Approved, true, false, false)]
    [InlineData(EntryKind.Held, false, false, true)]
    [InlineData(EntryKind.Sending, false, false, true)]
    [InlineData(EntryKind.Unknown, false, false, true)]
    [InlineData(EntryKind.Refused, false, true, false)]
    [InlineData(EntryKind.NotDone, false, true, false)]
    [InlineData(EntryKind.Revoked, false, true, false)]
    [InlineData(EntryKind.Started, false, false, false)]
    public void Each_kind_goes_under_the_iPad_s_filters(EntryKind kind, bool moneyMoved, bool stopped, bool waiting)
    {
        var line = Entry(kind, "€40");
        Assert.True(RecordScreen.Keeps(RecordFilter.All, line));
        Assert.Equal(moneyMoved, RecordScreen.Keeps(RecordFilter.MoneyMoved, line));
        Assert.Equal(stopped, RecordScreen.Keeps(RecordFilter.Stopped, line));
        Assert.Equal(waiting, RecordScreen.Keeps(RecordFilter.Waiting, line));
        Assert.False(RecordScreen.Keeps(RecordFilter.MoneyMoved, Entry(kind)), "no amount, no money moved");
    }

    [Theory]
    [InlineData(EntryKind.Done, Dot.Went)]
    [InlineData(EntryKind.Approved, Dot.Went)]
    [InlineData(EntryKind.Started, Dot.Went)]
    [InlineData(EntryKind.Held, Dot.Waiting)]
    [InlineData(EntryKind.Sending, Dot.Waiting)]
    [InlineData(EntryKind.Unknown, Dot.Waiting)]
    [InlineData(EntryKind.Refused, Dot.Stopped)]
    [InlineData(EntryKind.NotDone, Dot.Stopped)]
    [InlineData(EntryKind.Revoked, Dot.Stopped)]
    public void Each_kind_has_its_dot(EntryKind kind, Dot dot) => Assert.Equal(dot, RecordScreen.DotOf(kind));

    [Fact]
    public void Days_are_newest_first_and_named_today_yesterday_or_by_weekday()
    {
        var entries = new[] { Entry(EntryKind.Done, hoursAgo: 0), Entry(EntryKind.Done, hoursAgo: 12), Entry(EntryKind.Done, hoursAgo: 60) };
        var days = RecordScreen.Days(En, entries, RecordFilter.All, null, Now, TimeZoneInfo.Utc);
        Assert.Equal(new[] { "TODAY · NOV 15", "YESTERDAY · NOV 14", "THURSDAY · NOV 12" }, days.Select(d => d.Title));
        Assert.Equal(entries[0], days[0].Entries[0]);
    }

    [Fact]
    public void One_taf_narrows_it_and_an_empty_Record_says_why()
    {
        var entries = new[] { Entry(EntryKind.Done, taf: "Refunds"), Entry(EntryKind.Done, taf: "Access") };
        Assert.Single(RecordScreen.Days(En, entries, RecordFilter.All, "Access", Now, TimeZoneInfo.Utc).Single().Entries);
        Assert.Equal("Nothing yet.", RecordScreen.Empty(En, RecordFilter.All, null));
        Assert.Equal("Nothing here.", RecordScreen.Empty(En, RecordFilter.Waiting, null));
        Assert.Equal("Nothing here.", RecordScreen.Empty(En, RecordFilter.All, "Access"));
    }

    [Fact]
    public void A_line_is_its_taf_then_what_with_the_time_in_24_hours()
    {
        var paris = TimeZoneInfo.CreateCustomTimeZone("Paris", TimeSpan.FromHours(1), "Paris", "Paris");
        Assert.Equal(("Refunds · Refund order #4471", "01:17"), RecordScreen.Line(Entry(EntryKind.Done), paris));
    }

    [Fact]
    public void The_sample_is_the_reference_set_s_Record()
    {
        var sample = RecordScreen.Sample(En, Now);
        Assert.Equal(15, sample.Count);
        Assert.Equal(("Chargebacks", "Accept dispute on #3342", "€35, within the Card"), (sample[0].Taf, sample[0].Title, sample[0].Detail));
        Assert.Equal(("Held: Run migration 0042_orders_index", "Schema migrations: always asks you", EntryKind.Held), (sample[1].Title, sample[1].Detail, sample[1].Kind));
        Assert.Equal(("Refunds started", "Card issued by You", EntryKind.Started), (sample[10].Title, sample[10].Detail, sample[10].Kind));
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, new[] { "Chargebacks", "Deploys", "Access", "Bill pay", "Refunds" }.Select(t => sample.First(e => e.Taf == t).TafColor));
        Assert.Equal(5, sample.Count(e => RecordScreen.Keeps(RecordFilter.MoneyMoved, e)));
        Assert.Equal(sample, sample.OrderByDescending(e => e.At));
        Assert.Equal(new[] { "Refunds", "Bill pay", "Access", "Deploys", "Chargebacks" }, RecordScreen.TafChips(sample).Select(c => c.Taf));
    }

    [Theory]
    [MemberData(nameof(Shipped))]
    public void Every_line_is_there_in_every_shipped_language(string language)
    {
        var w = WordBook.Load(SharedFiles.Strings, language);
        var days = RecordScreen.Days(w, RecordScreen.Sample(w, Now), RecordFilter.All, null, Now, TimeZoneInfo.Utc);
        var all = days.Select(d => d.Title)
            .Concat(days.SelectMany(d => d.Entries).SelectMany(e => new[] { e.Title, e.Detail }))
            .Concat(RecordScreen.Filters(w).Select(f => f.Title))
            .Concat([RecordScreen.Heading(w).Title, RecordScreen.Heading(w).Lead, RecordScreen.AllTafs(w), RecordScreen.Empty(w, RecordFilter.Stopped, null)]);
        Assert.All(all, line => { Assert.False(string.IsNullOrWhiteSpace(line)); Assert.DoesNotContain("{", line); });
    }
}
