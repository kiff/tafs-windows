using Tafs.Core.Home;

namespace Tafs.Core.Tests.Home;

[Part("home")]
public class PageWidthsTests
{
    [Theory]
    [InlineData(PageKind.Reading, 720)]
    [InlineData(PageKind.List, 980)]
    [InlineData(PageKind.Gallery, 640)]
    [InlineData(PageKind.Brief, 1100)]
    [InlineData(PageKind.Calendar, 1180)]
    public void Each_kind_has_the_iPad_s_and_Mac_s_cap(PageKind kind, double cap) => Assert.Equal(cap, PageWidths.Cap(kind));

    [Fact]
    public void On_a_big_screen_the_column_is_capped_and_centred() =>
        // A 1920 screen less the 300 sidebar: the 720 column sits in the middle, the same space either side.
        Assert.Equal((450, 720), PageWidths.Place(1620, PageKind.Reading));

    [Fact]
    public void A_window_narrower_than_the_cap_gives_the_column_all_of_it() =>
        Assert.Equal((0, 900), PageWidths.Place(900, PageKind.Calendar));

    [Fact]
    public void Nothing_to_place_in_is_nothing() => Assert.Equal((0, 0), PageWidths.Place(0, PageKind.List));
}
