using System.Text;
using System.Text.Json;
using Tafs.Core.Account;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Account;

/// <summary>Account › Sign-in, the email-code setting, and How tafs works' sections after the cards, as the iPad's.</summary>
[Part("account")]
public class CodeSignInTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static CodeSignIn? Read(string json) => CodeSignIn.Read(JsonDocument.Parse(json).RootElement);

    [Theory]
    [InlineData("""{"email_code": true, "available": true}""", true, true)]
    [InlineData("""{"email_code": false, "available": true}""", false, true)]
    [InlineData("""{"email_code": false}""", false, false)]
    [InlineData("""{"email_code": false, "available": "yes"}""", false, false)]
    public void Kiff_s_answer_reads_with_available_false_unless_said(string json, bool on, bool available) =>
        Assert.Equal(new CodeSignIn(on, available), Read(json));

    [Theory]
    [InlineData("""{"available": true}""")]
    [InlineData("""{"email_code": "true"}""")]
    [InlineData("[]")]
    public void Without_a_boolean_email_code_it_can_t_be_read(string json) => Assert.Null(Read(json));

    /// <summary>On: Turn off from here. Off and available: Turn on opens KIFF's page. Off and not available: said so, no button.</summary>
    [Fact]
    public void The_row_offers_what_the_setting_allows()
    {
        var on = new CodeSignIn(true, true).Lines(En);
        Assert.Equal(("Sign in with a code sent by email", "On", CodeSignInAction.TurnOff, "Turn off", (string?)null),
            (on.Label, on.Value, on.Action, on.Button, on.Note));
        var off = new CodeSignIn(false, true).Lines(En);
        Assert.Equal(("Off", CodeSignInAction.TurnOn, "Turn on", "Turning it on opens KIFF's sign-in page, where you confirm it's you."),
            (off.Value, off.Action, off.Button, off.Note));
        var none = new CodeSignIn(false, false).Lines(En);
        Assert.Equal((CodeSignInAction.None, (string?)null, "Signing in with a code isn't available yet."), (none.Action, none.Button, none.Note));
        Assert.StartsWith("When it's on, you can sign in with a code", on.Help);
    }

    [Fact]
    public void Off_is_the_only_change_sent_and_on_opens_kiff_s_own_page()
    {
        Assert.Equal("""{"email_code":false}""", Encoding.UTF8.GetString(CodeSignIn.OffBody));
        Assert.Equal("https://kiff.dev/dashboard/sign-in", CodeSignIn.KiffPage.AbsoluteUri);
        Assert.Equal("/v1/me/sign-in", CodeSignIn.Path);
    }

    /// <summary>How tafs works says only what the person can use: Chrome where planque offers it, the code where KIFF can send one.</summary>
    [Fact]
    public void How_tafs_works_shows_chrome_and_the_code_only_where_they_apply()
    {
        Assert.Empty(HelpSections.Shown(En, chromeOffered: false, codeAvailable: false));
        var both = HelpSections.Shown(En, chromeOffered: true, codeAvailable: true);
        Assert.Equal(["chrome", "code"], both.Select(s => s.Id));
        Assert.Equal("tafs can work in your Chrome", both[0].Title);
        Assert.Contains("Needs you", both[0].Lead);
        Assert.Contains("Account › Chrome", both[0].More);
        Assert.Equal("Sign in with a code sent by email", both[1].Title);
        Assert.Contains("Account › Sign-in", both[1].Lead);
        Assert.Equal("Never give this code to anyone. KIFF will never ask you for it.", both[1].More);
        Assert.Equal(["code"], HelpSections.Shown(En, chromeOffered: false, codeAvailable: true).Select(s => s.Id));
        Assert.All(both, s => Assert.DoesNotContain("{", s.Lead + s.More));
    }
}
