using System.Text.Json;
using Tafs.Core.NeedsYou;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.NeedsYou;

/// <summary>Home's count of what waits, as the iPad's needsYouCount and needsYouWords.</summary>
[Part("approvals")]
public class NeedsYouTotalTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void Home_s_count_and_words_less_what_the_app_leaves_out_else_its_own()
    {
        var home = NeedsYouTotal.Read(Json("""{"needs_you": {"count": 3, "words": "3 need you."}}"""))!;
        Assert.Equal((3, "3 need you."), (NeedsYouTotal.Shown(home, 0, 9), NeedsYouTotal.Line(En, home, 0, 3)));
        // The chat open in Coach isn't counted: the app says the rest in its own words.
        Assert.Equal((2, "2 need you."), (NeedsYouTotal.Shown(home, 1, 9), NeedsYouTotal.Line(En, home, 1, 2)));
        Assert.Equal("1 needs you.", NeedsYouTotal.Line(En, home, 2, 1));
        Assert.Equal("Nothing needs you.", NeedsYouTotal.Line(En, null, 0, 0));
        Assert.Equal(0, NeedsYouTotal.Shown(home, 5, 9));
        // None from tafs cloud: the app's own count.
        Assert.Null(NeedsYouTotal.Read(Json("""{"needs_you": {"words": "no count"}}""")));
        Assert.Null(NeedsYouTotal.Read(Json("""{}""")));
        Assert.Equal(4, NeedsYouTotal.Shown(null, 0, 4));
    }

    [Fact]
    public void The_Overview_names_the_rest_only_when_more_waits_than_its_held_calls()
    {
        Assert.True(NeedsYouTotal.ShowsRest(3, 0));
        Assert.True(NeedsYouTotal.ShowsRest(3, 2));
        Assert.False(NeedsYouTotal.ShowsRest(2, 2));
        Assert.False(NeedsYouTotal.ShowsRest(0, 0));
    }
}
