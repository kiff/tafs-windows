using Tafs.Core.SignIn;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.SignIn;

[Part("pairing")]
public class SignInScreenTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 14, 22, 0, 0, TimeSpan.Zero);
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static readonly SignInCode Code = new("dc-secret", "ABCD-EFGH", new Uri("https://kiff.dev/device"),
        new Uri("https://kiff.dev/device?code=ABCD-EFGH"), Now.AddMinutes(10), TimeSpan.FromSeconds(5));

    public static IEnumerable<object[]> Shipped() => WordBook.Shipped(SharedFiles.Strings).Select(l => new object[] { l });

    [Fact]
    public void The_welcome_offers_KIFF_or_the_phone()
    {
        var lines = SignInScreen.Lines(En, SignInStep.Welcome);
        Assert.Equal("Sign in with KIFF", lines.Button);
        Assert.Contains("Approve this Windows PC in KIFF", lines.Lead);
        Assert.Equal("Already use tafs on your phone? Add this Windows PC with it.", lines.PairInstead);
        Assert.Null(lines.Code);
    }

    [Fact]
    public void While_waiting_it_shows_the_code_to_check_and_the_link()
    {
        var lines = SignInScreen.Lines(En, SignInStep.Waiting, Code);
        Assert.Equal("ABCD-EFGH", lines.Code);
        Assert.Equal("Your code", lines.CodeLabel);
        Assert.Equal("Waiting for your browser… Check that it shows ABCD-EFGH, and approve only with this Windows PC in your hands.", lines.Status);
        Assert.Equal("Open KIFF to approve", lines.Button);
        Assert.Equal("https://kiff.dev/device?code=ABCD-EFGH", lines.Link);
        Assert.Null(lines.PairInstead);
        Assert.DoesNotContain("dc-secret", string.Join("|", lines.Lead, lines.Status, lines.Link));
    }

    [Fact]
    public void A_failure_says_KIFF_s_status_and_offers_to_start_again()
    {
        var why = SignInScreen.Why(En, DeviceSignIn.ReadToken(403, """{"error":"access_denied"}"""));
        var lines = SignInScreen.Lines(En, SignInStep.CouldNotFinish, why: why);
        Assert.Equal("Couldn't finish: KIFF answered 403. Try again.", lines.Status);
        Assert.Equal("Sign in with KIFF", lines.Button);
        Assert.Equal("Couldn't start: KIFF answered ?. Try again.",
            SignInScreen.Lines(En, SignInStep.CouldNotStart, why: SignInScreen.WhyStart(En, 0)).Status);
    }

    [Theory]
    [InlineData(SignInStatus.Pending, SignInStep.Waiting)]
    [InlineData(SignInStatus.SlowDown, SignInStep.Waiting)]
    [InlineData(SignInStatus.Expired, SignInStep.Expired)]
    [InlineData(SignInStatus.Failed, SignInStep.CouldNotFinish)]
    public void Each_answer_moves_the_screen(SignInStatus status, SignInStep step) =>
        Assert.Equal(step, SignInScreen.After(status));

    [Fact]
    public void Signed_in_closes_the_screen() => Assert.Null(SignInScreen.After(SignInStatus.SignedIn));

    [Theory]
    [MemberData(nameof(Shipped))]
    public void Every_line_is_filled_in_every_shipped_language(string language)
    {
        var w = WordBook.Load(SharedFiles.Strings, language);
        var all = Enum.GetValues<SignInStep>()
            .Select(step => SignInScreen.Lines(w, step, Code, "x"))
            .SelectMany(l => new[] { l.Title, l.Lead, l.CodeLabel, l.Status, l.Button, l.OtherBrowser, l.PairInstead })
            .OfType<string>();
        Assert.All(all, line => Assert.DoesNotContain("{", line));
    }
}
