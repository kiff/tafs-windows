using System.Text.Json;
using Tafs.Core.Contracts;
using Tafs.Core.Record;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

[Part("record")]
public class TafActivityTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 15, 2, 56, 30, TimeSpan.Zero);
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    public static IEnumerable<object[]> Shipped() => WordBook.Shipped(SharedFiles.Strings).Select(l => new object[] { l });

    [Fact]
    public void The_contract_reads_as_tafs_cloud_wrote_it()
    {
        var page = TafActivity.Read(ContractFile.Answer(SharedFiles.Contract("taf-activity.json")));

        var day = Assert.Single(page.Groups);
        Assert.Equal("Today", day.Title);
        Assert.Equal(new ActivityLine("510f2ea5-2afc-5ec7-84d0-52e3104c4479", EntryKind.Done, "Read a case", "Done, within its Card", "12:00"),
            Assert.Single(day.Lines));
        Assert.Equal(new[] { "all", "done", "held", "refused" }, page.Filters.Select(f => f.Id));
        Assert.Equal(new[] { "All", "Done", "Held", "Refused" }, page.Filters.Select(f => f.Title));
        Assert.Equal("all", page.Filter);
        Assert.Equal("Nothing older in this view", page.EndWords);
        Assert.Equal(64, page.Revision?.Length);
    }

    [Fact]
    public void A_kind_it_doesnt_know_waits_and_what_it_cant_read_is_left_out()
    {
        var page = TafActivity.Read(JsonDocument.Parse("""
            {"groups": [{"title": "Today", "entries": [
              {"id": "a", "kind": "paused_for_review", "title": "Refund order #4502", "time": "09:00", "amount_text": "€40"},
              {"id": "b", "kind": "done"},
              {"id": "c", "kind": "done", "title": 7}
            ]}, {"title": "Yesterday", "entries": "none"}], "filters": [{"id": "all"}]}
            """).RootElement);

        var line = Assert.Single(Assert.Single(page.Groups).Lines);
        Assert.Equal(new ActivityLine("a", EntryKind.Unknown, "Refund order #4502", "", "09:00", "€40"), line);
        Assert.Empty(page.Filters);
        Assert.Equal("all", page.Filter);
        Assert.Null(page.EndWords);
        Assert.Empty(TafActivity.Read(JsonDocument.Parse("[]").RootElement).Groups);
    }

    [Theory]
    [InlineData(EntryKind.Done, true, false, false)]
    [InlineData(EntryKind.Approved, true, false, false)]
    [InlineData(EntryKind.Held, false, true, false)]
    [InlineData(EntryKind.Sending, false, false, false)]
    [InlineData(EntryKind.Refused, false, false, true)]
    [InlineData(EntryKind.NotDone, false, false, true)]
    [InlineData(EntryKind.Revoked, false, false, true)]
    [InlineData(EntryKind.Started, false, false, false)]
    public void Each_kind_goes_under_the_iPad_s_Activity_filters(EntryKind kind, bool done, bool held, bool refused)
    {
        Assert.True(TafActivity.Keeps("all", kind));
        Assert.Equal(done, TafActivity.Keeps("done", kind));
        Assert.Equal(held, TafActivity.Keeps("held", kind));
        Assert.Equal(refused, TafActivity.Keeps("refused", kind));
    }

    /// <summary>The reference set's taf.png: Support replies' three lines today, newest first, at 24-hour times.</summary>
    [Fact]
    public void The_sample_is_Support_replies_own_lines_of_the_Record()
    {
        var page = TafActivity.Sample(En, "Support replies", "all", Now, TimeZoneInfo.Utc);

        var day = Assert.Single(page.Groups);
        Assert.Equal("TODAY · NOV 15", day.Title);
        Assert.Equal(new[] { "Held: Promise a replacement on ticket #7802", "Reply to ticket #7781", "Support replies started" }, day.Lines.Select(l => l.Title));
        Assert.Equal(new[] { "Promises: always asks you", "Within the Card", "Card issued by You" }, day.Lines.Select(l => l.Detail));
        Assert.All(day.Lines, l => Assert.Equal("02:56", l.Time));
        Assert.Equal(new[] { "All", "Done", "Held", "Refused" }, page.Filters.Select(f => f.Title));
    }

    [Fact]
    public void Done_is_the_iPad_s_taf_page_word_not_the_Record_s() =>
        Assert.Equal(new[] { "Tout", "Terminé", "Retenus", "Refusés" },
            TafActivity.Filters(WordBook.Load(SharedFiles.Strings, "fr")).Select(f => f.Title));

    [Fact]
    public void A_filter_narrows_the_sample_and_says_so_when_nothing_is_left()
    {
        var done = TafActivity.Sample(En, "Support replies", "done", Now, TimeZoneInfo.Utc);
        Assert.Single(Assert.Single(done.Groups).Lines);
        Assert.Equal("done", done.Filter);

        Assert.Empty(TafActivity.Sample(En, "Support replies", "refused", Now, TimeZoneInfo.Utc).Groups);
        Assert.Equal("Nothing here.", TafActivity.Empty(En, "refused"));
        Assert.Equal("Nothing yet.", TafActivity.Empty(En, "all"));
    }

    [Theory]
    [MemberData(nameof(Shipped))]
    public void The_sample_has_its_lines_in_every_shipped_language(string language)
    {
        var w = WordBook.Load(SharedFiles.Strings, language);
        var page = TafActivity.Sample(w, TafScreen.SampleTaf(w), "all", Now, TimeZoneInfo.Utc);
        Assert.Equal(3, Assert.Single(page.Groups).Lines.Count);
        Assert.All(page.Filters, f => Assert.False(string.IsNullOrWhiteSpace(f.Title)));
    }

    /// <summary>The page's Card-unit requests and their notice (the iPad's TafActivityList); none sent is none, so Home's stand in.</summary>
    [Fact]
    public void A_page_carries_its_Card_unit_requests_and_their_notice()
    {
        var page = TafActivity.Read(JsonDocument.Parse("""
            {"groups": [], "card_unit_exceptions": [{"id": "ce_7", "title": "A bigger unit", "words": "It asks for €200.", "dismiss_title": "Dismiss"}, {"id": "ce_8"}],
             "card_unit_exceptions_notice": "  Dismissing one tells Refunds no.  "}
            """).RootElement);
        Assert.Equal(["ce_7"], page.CardUnits!.Select(a => a.Id));
        Assert.Equal("Dismissing one tells Refunds no.", page.CardUnitsNotice);
        // None sent: null, not empty, so Home's list for the taf stands in.
        var none = TafActivity.Read(JsonDocument.Parse("""{"groups": []}""").RootElement);
        Assert.Null(none.CardUnits);
        Assert.Null(none.CardUnitsNotice);
        Assert.Empty(TafActivity.Read(JsonDocument.Parse("""{"card_unit_exceptions": []}""").RootElement).CardUnits!);
    }
}
