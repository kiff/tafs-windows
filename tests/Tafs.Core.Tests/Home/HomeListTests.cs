using System.Text.Json;
using Tafs.Core.Contracts;
using Tafs.Core.Home;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Home;

[Part("home")]
public class HomeListTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    static readonly DateTimeOffset SameDay = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    static HomeAnswer Home(string file) => HomeList.Read(En, ContractFile.Answer(SharedFiles.Contract(file)), TimeZoneInfo.Utc);

    static HomeAnswer Home(string json, TimeZoneInfo zone) => HomeList.Read(En, JsonDocument.Parse(json).RootElement, zone);

    [Fact]
    public void A_live_taf_reads_with_its_next_run_its_Card_and_what_went_wrong()
    {
        var home = Home("home.json");

        var taf = Assert.Single(home.Tafs);
        Assert.Equal(("taf-aaaa0000000000000000", "Rae", 0, "working"), (taf.Id, taf.Name, taf.Colour, taf.StateKey));
        // Its KIFF agent: what Coach's result card names the taf by.
        Assert.Equal("rae", taf.Agent);
        Assert.Equal("Next run Fri 12:30", taf.Now);
        Assert.Equal("Its last 2 checks found work too big for one run, so nothing ran", taf.Attention);
        Assert.Equal("card_rae", Assert.Single(taf.Cards).Id);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero), taf.LastChange);
        Assert.Equal("Ask Coach to describe this taf's job", taf.Job);
        Assert.Null(taf.LastRun);
    }

    /// <summary>Home's headline is tafs cloud's count: a taf whose work needs looking at is neither working nor paused.</summary>
    [Fact]
    public void The_headline_is_tafs_cloud_s_never_counted_here()
    {
        Assert.Equal("0 tafs working.", Home("home.json").Headline);
        Assert.Null(Home("""{"tafs": [], "headline": {"words": ""}}""", TimeZoneInfo.Utc).Headline);
    }

    [Fact]
    public void A_paused_taf_says_Paused_with_no_next_run_and_no_line_of_what_went_wrong()
    {
        var taf = Assert.Single(Home("home-paused.json").Tafs);

        Assert.Equal(("paused", "Paused"), (taf.StateKey, taf.Now));
        Assert.Null(taf.Attention);
    }

    /// <summary>The Overview: every taf, what it's doing, its Card's figure and words.</summary>
    [Fact]
    public void The_Overview_s_line_says_its_next_run_and_what_its_Card_has_left()
    {
        var row = Assert.Single(HomeList.Rows(En, Home("home.json"), TimeZoneInfo.Utc));

        Assert.Equal(("taf-aaaa0000000000000000", "Rae", "Next run Fri 12:30", "Nothing yet"), (row.Key, row.Taf, row.Now, row.LastResult));
        var read = new Dictionary<string, LatestRun> { ["taf-aaaa0000000000000000"] = new(Text: "3 refunds sent") };
        Assert.Equal("3 refunds sent", HomeList.Rows(En, Home("home.json"), TimeZoneInfo.Utc, read)[0].LastResult);
        Assert.StartsWith("€800 ", row.Card);
    }

    /// <summary>Your tafs: changed in the last 24 hours, with what went wrong where its next run would be; not two days on.</summary>
    [Fact]
    public void Your_tafs_lists_those_relevant_now_with_what_went_wrong()
    {
        var home = Home("home.json");

        Assert.Equal("Its last 2 checks found work too big for one run, so nothing ran",
            Assert.Single(HomeList.YourTafs(En, home, SameDay)).Now);
        Assert.Empty(HomeList.YourTafs(En, home, SameDay.AddDays(2)));
    }

    /// <summary>With no job from Coach, as the iPad's: an agent tafs doesn't run is described by its Cards.</summary>
    [Fact]
    public void With_no_job_it_works_under_its_Cards() =>
        Assert.Equal("Works under 2 KIFF Cards.",
            Home("""{"tafs": [{"id": "t", "name": "T", "card": {"ids": ["a", "b"]}}]}""", TimeZoneInfo.Utc).Tafs[0].Job);

    [Theory]
    [InlineData("""{"state": "waiting", "waiting": 1}""", "Waiting for you", "waiting")]
    [InlineData("""{"state": "halted"}""", "Halted · resume it from Halt", "halted")]
    [InlineData("""{"state": "working", "unscheduled": true}""", "Works from its chat", "working")]
    [InlineData("""{"state": "working", "next_run": "2026-10-16T12:30:00Z"}""", "Next run 14:30", "working")]
    [InlineData("""{"state": "working"}""", "Running", "working")]
    [InlineData("""{"state": "idle"}""", "Acting under KIFF", "ready")]
    [InlineData("""{"state": "revoked"}""", "Card not active", "stopped")]
    public void Each_state_reads_as_the_iPad_says_it(string fields, string now, string head)
    {
        var json = $$"""{"tafs": [{{fields.TrimEnd('}')}}, "id": "t", "name": "T"}, {"id": "no name"}]}""";
        var taf = Assert.Single(Home(json, TimeZoneInfo.CreateCustomTimeZone("Paris+2", TimeSpan.FromHours(2), "Paris+2", "Paris+2")).Tafs);

        Assert.Equal((now, head), (taf.Now, taf.StateKey));
    }

    /// <summary>An idle taf that isn't healthy says why; a healthy one, or a line that's blank, says what it's doing.</summary>
    [Theory]
    [InlineData("failing", "  Nothing ran  ", "Nothing ran")]
    [InlineData("healthy", "Nothing ran", null)]
    [InlineData("failing", " ", null)]
    public void An_idle_taf_s_line_says_what_went_wrong_only_when_it_isn_t_healthy(string level, string line, string? attention)
    {
        var work = $$"""{"status": "idle", "line": "{{line}}"}""";
        var json = $$$"""{"tafs": [{"id": "t", "name": "T", "state": "idle", "work": {{{work}}}, "health": {"level": "{{{level}}}", "lines": ["why"]}}]}""";

        Assert.Equal(attention, Home(json, TimeZoneInfo.Utc).Tafs[0].Attention);
    }
}
