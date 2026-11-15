using Tafs.Core.Tests.Strings;
using Tafs.Core.Usage;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Usage;

[Part("calendar")]
public class UsageScreenTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    public static IEnumerable<object[]> Shipped() => WordBook.Shipped(SharedFiles.Strings).Select(l => new object[] { l });

    [Fact]
    public void The_balance_is_tafs_cloud_s_words_with_its_bar_and_tone()
    {
        var b = UsageScreen.ReadBalance("""
            {"balance":{"state":"warning","meter_pct":130,"words":"Solo · 40 of 500 credits left","detail":"Then 900 in packs",
             "warning":"Running low","raise":{"words":"Raise the limit","from_cents":5000}},
             "plan_line":"Solo · 40 credits left","plan_tone":"warning"}
            """)!;
        Assert.Equal(new Balance(Tone.Warning, "Solo · 40 of 500 credits left", 100, "Then 900 in packs", "Running low", "Raise the limit",
            true, "Solo · 40 credits left", Tone.Warning, null), b);
    }

    [Fact]
    public void A_field_it_can_t_read_is_left_out_never_the_read()
    {
        var b = UsageScreen.ReadBalance("""{"balance":{"state":"sideways","meter_pct":"half","words":"Solo","detail":"","raise":7,"credits_panel":false}}""")!;
        Assert.Equal(Tone.Ok, b.State);
        Assert.Null(b.MeterPct);
        Assert.Null(b.Detail);
        Assert.Null(b.Raise);
        Assert.False(b.CreditsPanel);
        Assert.Null(UsageScreen.ReadBalance("""{"balance":{"state":"ok"}}"""));
        Assert.True(UsageScreen.ReadBalance("""{"balance":{"words":"x"}}""")!.CreditsPanel); // a tafs cloud from before: the panel shows
    }

    [Fact]
    public void Credits_by_taf_Coach_s_paid_use_and_why_runs_weren_t_counted()
    {
        var c = UsageScreen.ReadCredits("""
            {"credits":1300,"runs":75,"words":"This period: 1,300 credits used",
             "tafs":[{"taf":"t1","credits":820,"runs":41,"words":"Refunds · 820 credits"},{"credits":20,"runs":1,"words":"Removed tafs · 20 credits"},{"credits":"3","words":"bad count"}],
             "coach":{"credits":60,"runs":6,"words":"Coach, past its fair use · 60 credits"},
             "not_counted":{"runs":3,"words":"Not counted: 3 runs","lines":[{"why":"trial","runs":2,"words":"Trials · 2 runs"},{"why":"failed"}]},
             "note":"Each run counts once."}
            """)!;
        Assert.Equal(["Refunds · 820 credits", "Removed tafs · 20 credits", "bad count"], c.Tafs.Select(t => t.Words));
        Assert.Equal(0, c.Tafs[2].Credits);
        Assert.Null(c.Tafs[1].Taf);
        Assert.Equal("Coach, past its fair use · 60 credits", c.Coach);
        Assert.Equal(["Trials · 2 runs"], c.NotCounted!.Lines.Select(l => l.Words));
        Assert.True(UsageScreen.ShowsNotCounted(c));
        Assert.Equal("Each run counts once.", c.Note);
        Assert.Null(UsageScreen.ReadCredits("""{"words":"no total"}"""));
        Assert.False(UsageScreen.ShowsNotCounted(UsageScreen.ReadCredits("""{"credits":0,"words":"x","not_counted":{"runs":0}}""")!));
    }

    [Fact]
    public void The_sample_is_the_iPad_s()
    {
        var b = UsageScreen.SampleBalance(En);
        Assert.Equal("Solo · 260 of 500 credits left this period", b.Words);
        Assert.Equal(48, b.MeterPct);
        var c = UsageScreen.SampleCredits(En);
        Assert.Equal("This period: 1,240 credits used · ends on 30 Nov", c.Words);
        Assert.Equal(3, c.NotCounted!.Lines.Count);
        Assert.Null(UsageScreen.SampleCredits(En, previous: true).NotCounted);
    }

    [Theory]
    [MemberData(nameof(Shipped))]
    public void Every_line_is_filled_in_every_shipped_language(string language)
    {
        var w = WordBook.Load(SharedFiles.Strings, language);
        var c = UsageScreen.SampleCredits(w);
        var lines = new[] { UsageScreen.SampleBalance(w).Words, UsageScreen.SampleBalance(w).Detail!, c.Words, c.Note!, c.NotCounted!.Words,
            w.Get("usage.title"), w.Get("usage.credits"), w.Get("usage.this_period"), w.Get("usage.last_period") }
            .Concat(c.Tafs.Select(t => t.Words)).Concat(c.NotCounted.Lines.Select(l => l.Words));
        Assert.All(lines, l => Assert.DoesNotContain("{", l));
    }
}
