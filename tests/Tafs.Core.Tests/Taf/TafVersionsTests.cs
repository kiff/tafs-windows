using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>A taf's versions and its credits, as the iPad's VersionsPanel and TafCreditsView read them.</summary>
[Part("record")]
public class TafVersionsTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    const string Versions = """
        {"versions": [
           {"n": 1, "created": "2026-11-10T09:00:00Z", "live": false},
           {"n": 2, "note": "Asks before refunds over 50 EUR", "by_words": "By Coach mode", "created": "2026-11-12T14:30:00Z", "live": true,
            "trials": [{"started": "2026-11-11T10:00:00Z", "ended": "2026-11-12T10:00:00Z", "why": "installed", "calls": 3, "held": 1, "refused": 0}]},
           {"n": 3, "note": "Every hour", "by_words": "By you", "created": "2026-11-15T08:00:00Z", "live": false, "draft": "drf_3"},
           {"note": "no number"}
         ],
         "switching": {"n": 3, "draft": "drf_3", "at": "2026-11-15T08:05:00Z"}}
        """;

    [Fact]
    public void Its_versions_read_newest_first_the_live_one_marked()
    {
        var v = TafVersions.Read(JsonDocument.Parse(Versions).RootElement)!;
        Assert.Equal([3, 2, 1], v.Newest.Select(x => x.N));
        Assert.Equal(3, v.Switching);
        Assert.True(v.Versions.Single(x => x.N == 2).Live);
        Assert.Equal("By you · 15 Nov 2026, 08:00 · brings a new Card", TafVersions.Byline(En, v.Versions.Single(x => x.N == 3), TimeZoneInfo.Utc));
        Assert.Equal("10 Nov 2026, 09:00", TafVersions.Byline(En, v.Versions[0], TimeZoneInfo.Utc));
        Assert.Equal("Tried 11 Nov · 3 calls, 1 held · installed", TafVersions.Tried(En, v.Versions[1].Trials[0], TimeZoneInfo.Utc));
        Assert.Null(TafVersions.Read(JsonDocument.Parse("""{"switching": null}""").RootElement));
        Assert.Equal("/v1/tafs/t1/versions", TafVersions.Path("t1"));
    }

    [Fact]
    public void Its_credits_read_page_by_page()
    {
        var first = TafCredits.Read(JsonDocument.Parse("""
            {"words": "120 credits in 6 runs", "items": [{"run": "r2", "at": "2026-11-16T09:00:00Z", "words": "34 credits", "opens": true},
                                                     {"run": "r1", "at": "2026-11-15T09:00:00Z", "words": "Not counted: a trial", "opens": false}], "next": "c1"}
            """).RootElement)!;
        Assert.Equal(("120 credits in 6 runs", "c1"), (first.Words, first.Next));
        var more = first.With(TafCredits.Read(JsonDocument.Parse("""{"items": [{"run": "r1", "at": "2026-11-15T09:00:00Z"}, {"run": "r0", "at": "2026-11-14T09:00:00Z", "words": "12 credits"}]}""").RootElement)!);
        Assert.Equal(["r2", "r1", "r0"], more.Runs.Select(r => r.Run));
        Assert.Null(more.Next);
        Assert.Equal("/v1/usage/credits/tafs/t1?after=c%2B1", TafCredits.Path("t1", "c+1"));
    }
}
