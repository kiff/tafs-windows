using Tafs.Core.Home;

namespace Tafs.Core.Tests.Home;

/// <summary>The window opens near the iPad's landscape proportions, sized to the screen, 900 by 600 at the least.</summary>
[Part("home")]
public class WindowSizeTests
{
    [Fact]
    public void A_laptop_screen_opens_at_four_by_three_centred_in_its_work_area()
    {
        // 1920 by 1080 at 100%, taskbar of 48 at the foot: 85% of the height, 4:3.
        Assert.Equal((375, 77, 1170, 877), WindowSize.Opening(0, 0, 1920, 1032, 1));
        // A second screen to the right keeps its own origin.
        Assert.Equal((1920 + 375, 77, 1170, 877), WindowSize.Opening(1920, 0, 1920, 1032, 1));
    }

    [Fact]
    public void A_tall_screen_is_limited_by_its_width()
    {
        Assert.Equal((81, 389, 918, 688), WindowSize.Opening(0, 0, 1080, 1467, 1));
    }

    [Fact]
    public void Never_under_900_by_600_at_the_screens_scale_nor_over_the_work_area()
    {
        Assert.Equal((1350, 900), WindowSize.Minimum(1.5));
        Assert.Equal((900, 600), WindowSize.Minimum(1));
        // 1366 by 728 at 150%: the minimum is 1350 by 900, more than the screen's height, which it gets all of.
        Assert.Equal((8, 0, 1350, 728), WindowSize.Opening(0, 0, 1366, 728, 1.5));
        // 1280 by 752 at 100%: 85% is 639 high and 852 wide, so the minimum width of 900.
        Assert.Equal((190, 56, 900, 639), WindowSize.Opening(0, 0, 1280, 752, 1));
    }
}
