using Tafs.Core.Ci;

namespace Tafs.Core.Tests.Ci;

[Part("ci")]
public class ScreenSizeTests
{
    [Theory]
    [InlineData("1920x1080", 1920, 1080)]
    [InlineData(" 2560X1440 ", 2560, 1440)]
    [InlineData(null, 1280, 800)]
    [InlineData("", 1280, 800)]
    [InlineData("wide", 1280, 800)]
    [InlineData("1920", 1280, 800)]
    [InlineData("100x100", 1280, 800)]
    [InlineData("9000x9000", 1280, 800)]
    public void A_size_is_read_or_the_laptops(string? size, int width, int height) =>
        Assert.Equal((width, height), ScreenSize.Parse(size));
}
