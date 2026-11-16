using Tafs.Core.SignIn;

namespace Tafs.Core.Tests.SignIn;

/// <summary>What the window opens on, signed out and after signing in (the owner's approved mockup, 16 Nov).</summary>
[Part("pairing")]
public class SignedOutTests
{
    [Theory]
    [InlineData(null, false, false, false, Opening.SignedOut)]
    [InlineData(null, false, false, true, Opening.App)]
    [InlineData(null, false, true, false, Opening.App)]
    [InlineData(null, true, false, false, Opening.App)]
    [InlineData("record", false, false, false, Opening.App)]
    [InlineData("signed-out", true, false, true, Opening.SignedOut)]
    [InlineData("connected", true, false, false, Opening.Connected)]
    public void Signed_out_the_whole_window_signs_in(string? page, bool screenshot, bool sample, bool hasSession, Opening opens) =>
        Assert.Equal(opens, SignedOut.AtLaunch(page, screenshot, sample, hasSession));

    [Fact]
    public void Either_way_of_signing_in_ends_on_the_celebration_except_over_the_sample()
    {
        Assert.Equal(Opening.Connected, SignedOut.AfterSignIn(sample: false));
        Assert.Equal(Opening.App, SignedOut.AfterSignIn(sample: true));
    }
}
