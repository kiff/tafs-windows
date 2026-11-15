using Tafs.Core.Home;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Home;

[Part("home")]
public class OverviewTableTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 15, 0, 17, 30, TimeSpan.Zero);

    public static IEnumerable<object[]> Shipped() => WordBook.Shipped(SharedFiles.Strings).Select(l => new object[] { l });

    /// <summary>The reference set's overview.png: the five sample tafs, waiting ones first, each line as the iPad draws it.</summary>
    [Fact]
    public void The_sample_s_table_is_the_reference_s()
    {
        var rows = OverviewScreen.SampleRows(WordBook.Load(SharedFiles.Strings, "en"), Now);

        Assert.Equal(new[] { "Deploys", "Bill pay", "Refunds", "Chargebacks", "Access" }, rows.Select(r => r.Taf));
        Assert.Equal(new[] { 1, 3, 4, 0, 2 }, rows.Select(r => r.Colour));
        Assert.Equal(new[]
        {
            "Waiting for you · Run migration 0042_orders_index", "Waiting for you · Pay Atlas Freight",
            "Credit on account #2290", "Accept dispute on #3342", "Deactivate Tom's accounts",
        }, rows.Select(r => r.Now));
        Assert.Equal(new[]
        {
            "9 calls left today of 10", "€23,760 left this week of €25,000", "€1,960 left today of €2,000",
            "€465 left this week of €500", "39 calls left today of 40",
        }, rows.Select(r => r.Card));
        Assert.All(rows, r => Assert.Equal("–", r.LastResult));
    }

    /// <summary>overview.png's sidebar: only the tafs relevant now, the two a call of theirs waits on, each with what it's doing.</summary>
    [Fact]
    public void Your_tafs_in_the_sidebar_are_the_ones_relevant_now()
    {
        var rows = Sidebar.SampleYourTafs(WordBook.Load(SharedFiles.Strings, "en"), Now);
        Assert.Equal(new[] { "Deploys", "Bill pay" }, rows.Select(r => r.Taf));
        Assert.Equal("Waiting for you · Pay Atlas Freight", rows[1].Now);
    }

    [Theory]
    [InlineData(true, false, false, null, null, true)]
    [InlineData(false, true, false, null, null, true)]
    [InlineData(false, false, true, null, null, true)]
    [InlineData(false, false, false, 23.9, null, true)]
    [InlineData(false, false, false, null, 2.0, true)]
    [InlineData(false, false, false, 24.0, 30.0, false)]
    [InlineData(false, false, false, null, null, false)]
    public void A_taf_is_relevant_now_while_something_waits_or_it_ran_or_changed_within_a_day(
        bool held, bool step, bool knock, double? ranHoursAgo, double? changedHoursAgo, bool relevant) =>
        Assert.Equal(relevant, Sidebar.RelevantNow(held, step, knock,
            ranHoursAgo is { } r ? Now.AddHours(-r) : null, changedHoursAgo is { } c ? Now.AddHours(-c) : null, Now));

    [Theory]
    [MemberData(nameof(Shipped))]
    public void Every_line_is_there_in_every_shipped_language(string language)
    {
        var rows = OverviewScreen.SampleRows(WordBook.Load(SharedFiles.Strings, language), Now);
        Assert.Equal(5, rows.Count);
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Now) || r.Card.Contains('{') || r.Card == "–", r.Taf));
    }
}
