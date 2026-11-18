using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

[Part("record")]
public class TafScreenTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    public static IEnumerable<object[]> Shipped() => WordBook.Shipped(SharedFiles.Strings).Select(l => new object[] { l });

    [Fact]
    public void The_tabs_are_the_iPad_s_in_its_order()
    {
        Assert.Equal(new[] { TafTab.Chat, TafTab.Activity, TafTab.Card, TafTab.Setup }, TafScreen.Tabs(En).Select(t => t.Tab));
        Assert.Equal(new[] { "Chat", "Activity", "Card", "Setup" }, TafScreen.Tabs(En).Select(t => t.Title));
        Assert.Equal("View", TafScreen.TabsName(En));
    }

    [Fact]
    public void The_sample_head_is_Support_replies_working() =>
        Assert.Equal(new TafHead("Support replies", "S", 4, "Working"), TafScreen.SampleHead(En));

    [Theory]
    [InlineData("refunds", "R")]
    [InlineData("  élan", "É")]
    [InlineData("", "")]
    public void The_avatar_shows_the_name_s_first_letter(string name, string initial) =>
        Assert.Equal(initial, TafScreen.Initial(name));

    [Fact]
    public void The_sample_s_Card_bar_says_what_s_left_of_its_limit() =>
        Assert.Equal(new CardBarLine("28 calls left today of 40", "Support replies"), TafScreen.SampleCard(En));

    [Theory]
    [MemberData(nameof(Shipped))]
    public void Every_word_is_there_in_every_shipped_language(string language)
    {
        var w = WordBook.Load(SharedFiles.Strings, language);
        Assert.All(TafScreen.Tabs(w), t => Assert.False(string.IsNullOrWhiteSpace(t.Title)));
        Assert.DoesNotContain("{", TafScreen.SampleCard(w).Hero);
        Assert.False(string.IsNullOrWhiteSpace(TafScreen.SampleHead(w).State));
    }
}
