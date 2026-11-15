using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>
/// A halt as the iPad's TafsCloud.Halt and HaltScreen: its routes, each state read as sent, the words, who sees every taf's
/// halt, and who manages a taf as KIFF says.
/// </summary>
[Part("record")]
public class HaltTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static Halt? Read(string json) => Halt.Read(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void A_taf_s_halt_and_every_taf_s_have_their_routes()
    {
        Assert.Equal("/v1/tafs/taf_1/halt", Halt.Path("taf_1"));
        Assert.Equal("/v1/tafs/a%2Fb/halt", Halt.Path("a/b"));
        Assert.Equal("/v1/halt", Halt.Path(null));
    }

    /// <summary>Each state as sent; halted_at and stopped absent or null when not given; anything else fails the read.</summary>
    [Fact]
    public void A_halt_reads_as_sent()
    {
        Assert.Equal(Halt.Running, Read("""{"halt":{"state":"running","runtime":false,"kiff":false,"planque":false,"account":false}}"""));
        Assert.Equal(new Halt(HaltState.Halting, null, true, false, false, false, 2),
            Read("""{"halt":{"state":"halting","halted_at":null,"runtime":true,"kiff":false,"planque":false,"account":false,"stopped":2}}"""));
        Assert.Equal(new Halt(HaltState.Halted, new DateTimeOffset(2026, 11, 15, 18, 5, 0, TimeSpan.Zero), true, true, true, true),
            Read("""{"halt":{"state":"halted","halted_at":"2026-11-15T18:05:00Z","runtime":true,"kiff":true,"planque":true,"account":true}}"""));
        Assert.Null(Read("""{"halt":{"state":"paused","runtime":true,"kiff":true,"planque":true,"account":false}}"""));
        Assert.Null(Read("""{"halt":{"state":"halted","runtime":true,"kiff":true,"account":false}}"""));
        Assert.Null(Read("""{"halt":{"state":"halted","runtime":"yes","kiff":true,"planque":true,"account":false}}"""));
        Assert.Null(Read("""{"halt":{"state":"halted","halted_at":"soon","runtime":true,"kiff":true,"planque":true,"account":false}}"""));
        Assert.Null(Read("""{"state":"running"}"""));
    }

    /// <summary>The iPad's headline and explanation, for a taf and for every taf; since when, in this PC's time.</summary>
    [Fact]
    public void The_screen_s_words_are_the_iPad_s()
    {
        var since = new Halt(HaltState.Halted, new DateTimeOffset(2026, 11, 15, 18, 5, 0, TimeSpan.Zero), true, true, true, false);
        var halting = Halt.Running with { State = HaltState.Halting };
        Assert.Equal(("Running", "Halting stops it until you resume. A call already sent may still arrive."),
            (Halt.Headline(En, Halt.Running), Halt.Explanation(En, Halt.Running, false, TimeZoneInfo.Utc)));
        Assert.Equal("Halting stops your tafs until you resume. A call already sent may still arrive.", Halt.Explanation(En, Halt.Running, true, TimeZoneInfo.Utc));
        Assert.Equal(("Halting…", "Nothing new starts. Halted shows once KIFF and the connection both confirm."),
            (Halt.Headline(En, halting), Halt.Explanation(En, halting, false, TimeZoneInfo.Utc)));
        Assert.Equal(("Halted", "Since 18:05. Resuming starts only new work."), (Halt.Headline(En, since), Halt.Explanation(En, since, false, TimeZoneInfo.Utc)));
        Assert.Equal("Halted. Resuming starts only new work.", Halt.Explanation(En, since with { HaltedAt = null }, false, TimeZoneInfo.Utc));
        Assert.Equal(("Stop it until you resume", "Halted"), (Halt.RowLine(En, Halt.Running), Halt.RowLine(En, since)));
    }

    /// <summary>
    /// Every taf's halt shows to someone who manages Cards (cards.manage, or when KIFF doesn't say), while a taf runs, and
    /// whenever the account is halted or halting, tafs or not.
    /// </summary>
    [Fact]
    public void Who_sees_every_taf_s_halt()
    {
        var halted = Halt.Running with { State = HaltState.Halted };
        Assert.True(Halt.ShowsAccountHalt(true, true, Halt.Running));
        Assert.False(Halt.ShowsAccountHalt(true, false, Halt.Running));
        Assert.True(Halt.ShowsAccountHalt(true, false, halted));
        Assert.False(Halt.ShowsAccountHalt(false, true, halted));
        Assert.True(Halt.ManagesCards(["cards.manage"]));
        Assert.False(Halt.ManagesCards(["members.manage"]));
        Assert.True(Halt.ManagesCards(null));
    }

    /// <summary>Who manages a taf, by its agent, as KIFF's access says; an access that doesn't read says nothing.</summary>
    [Fact]
    public void Who_manages_a_taf()
    {
        static JsonElement J(string s) => JsonDocument.Parse(s).RootElement;
        var manage = CoachAccess.Manage(J("""{"agents":{"rae":{"use":true,"oversee":true,"manage":true},"setup":{"use":true,"oversee":false,"manage":false,"groups":["g1"]}}}"""))!;
        Assert.True(manage["rae"]);
        Assert.False(manage["setup"]);
        Assert.Null(CoachAccess.Manage(J("""{"agents":{"rae":{"use":true,"oversee":true}}}""")));
        Assert.Null(CoachAccess.Manage(J("""{"agents":{"rae":{"use":true,"oversee":true,"manage":"yes"}}}""")));
        Assert.Null(CoachAccess.Manage(null));
    }

    /// <summary>A revoked taf has no Halt row, as the iPad's taf.state != .revoked: tafs cloud's "stopped", or a state it doesn't know.</summary>
    [Theory]
    [InlineData("stopped", false)]
    [InlineData("revoked", false)]
    [InlineData("something new", false)]
    [InlineData("working", true)]
    [InlineData("idle", true)]
    [InlineData("halted", true)]
    [InlineData("paused", true)]
    public void A_revoked_taf_has_no_halt_row(string state, bool row) =>
        Assert.Equal(row, Halt.HasRow(new Tafs.Core.Home.HomeTaf("t", "Refunds", 0, state, "", null, [], 0, null, null, Agent: "ag")));
}
