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

    [Theory]
    [MemberData(nameof(Shipped))]
    public void Every_line_is_there_in_every_shipped_language(string language)
    {
        var rows = OverviewScreen.SampleRows(WordBook.Load(SharedFiles.Strings, language), Now);
        Assert.Equal(5, rows.Count);
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Now) || r.Card.Contains('{') || r.Card == "–", r.Taf));
    }
}
