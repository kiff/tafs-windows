using Tafs.Core.Coach;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Coach;

[Part("coach")]
public class CoachScreenTests
{
    public static IEnumerable<object[]> Shipped() => WordBook.Shipped(SharedFiles.Strings).Select(l => new object[] { l });

    [Fact]
    public void Before_Coach_starts_the_intro_says_how_this_PC_asks()
    {
        var intro = CoachScreen.Intro(WordBook.Load(SharedFiles.Strings, "en"));
        Assert.Equal(new CoachIntro("Coach", "Coach mode",
            "Name an app for your taf, like your payment provider. You approve with Windows Hello.", "Start",
            "Starting asks for Windows Hello once."), intro);
    }

    [Theory]
    [InlineData(1280, 426.666)]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    public void The_panel_opens_at_a_third_of_the_window(double window, double width) =>
        Assert.Equal(width, CoachScreen.PanelWidth(window), 2);

    [Theory]
    [MemberData(nameof(Shipped))]
    public void Every_line_is_filled_and_names_Windows_Hello_in_every_shipped_language(string language)
    {
        var intro = CoachScreen.Intro(WordBook.Load(SharedFiles.Strings, language));
        Assert.All(new[] { intro.Title, intro.Heading, intro.Line, intro.Start, intro.Footnote }, l => Assert.DoesNotContain("{", l));
        Assert.Contains(CoachScreen.Biometry, intro.Line);
        Assert.Contains(CoachScreen.Biometry, intro.Footnote);
    }
}
