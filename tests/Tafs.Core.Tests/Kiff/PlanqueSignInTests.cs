using Tafs.Core.Kiff;

namespace Tafs.Core.Tests.Kiff;

/// <summary>planque's return after a vendor's sign-in, as the iPad's <c>PlanqueClient.signedIn(from:)</c> reads it.</summary>
[Part("pairing")]
public class PlanqueSignInTests
{
    [Fact]
    public void The_return_names_the_account_the_grant_and_the_code()
    {
        Assert.Equal(new PlanqueSignedIn("t_1", "g 1", "abc"),
            PlanqueSignIn.Read(new Uri("tafs://planque/signed-in?tenant=t_1&grant=g%201&confirm=abc")));
        // The first of a name written twice; other names are no matter.
        Assert.Equal(new PlanqueSignedIn("t_1", "g_1", "abc"),
            PlanqueSignIn.Read(new Uri("tafs://planque/signed-in?confirm=abc&grant=g_1&tenant=t_1&grant=g_2&x=y")));
    }

    [Theory]
    [InlineData("tafs://planque/signed-in?tenant=t&grant=g")]
    [InlineData("tafs://planque/signed-in?tenant=t&grant=g&confirm=")]
    [InlineData("tafs://planque/other?tenant=t&grant=g&confirm=c")]
    [InlineData("tafs://kiff/signed-in?tenant=t&grant=g&confirm=c")]
    [InlineData("https://planque/signed-in?tenant=t&grant=g&confirm=c")]
    public void Any_other_url_is_none(string url) => Assert.Null(PlanqueSignIn.Read(new Uri(url)));
}
