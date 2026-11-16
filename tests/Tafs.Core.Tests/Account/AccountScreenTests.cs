using Tafs.Core.Account;
using Tafs.Core.Kiff;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Account;

[Part("account")]
public class AccountScreenTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    static readonly DateTimeOffset Now = new(2026, 11, 15, 4, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> Shipped() => WordBook.Shipped(SharedFiles.Strings).Select(l => new object[] { l });

    static KeptSession Kept(ApprovalMode mode) =>
        KeptSession.Of(new Uri("https://api.kiff.dev"), "t", "t1", "s1", null, mode, TimeSpan.FromDays(90), Now, "tafs device key");

    [Fact]
    public void The_mode_follows_where_this_PC_stands()
    {
        Assert.Equal(AccountMode.Sample, AccountScreen.ModeOf(sample: true, Kept(ApprovalMode.Passkey)));
        Assert.Equal(AccountMode.Kiff, AccountScreen.ModeOf(sample: false, Kept(ApprovalMode.View)));
        Assert.Equal(AccountMode.SignedOut, AccountScreen.ModeOf(sample: false, null));
        Assert.Equal(["Sample data", "Signed in with KIFF", "Signed out"],
            new[] { AccountMode.Sample, AccountMode.Kiff, AccountMode.SignedOut }.Select(m => AccountScreen.ModeWords(En, m)));
    }

    [Fact]
    public void View_only_exactly_when_the_session_says_view()
    {
        Assert.True(AccountScreen.ViewOnly(AccountMode.Kiff, Kept(ApprovalMode.View)));
        Assert.False(AccountScreen.ViewOnly(AccountMode.Kiff, Kept(ApprovalMode.Passkey)));
        Assert.False(AccountScreen.ViewOnly(AccountMode.Kiff, Kept(ApprovalMode.Key)));
        Assert.False(AccountScreen.ViewOnly(AccountMode.Sample, Kept(ApprovalMode.View)));
    }

    [Fact]
    public void The_actions_as_on_the_iPad_sign_out_red_and_asked_first()
    {
        Assert.Equal([("Sign in with KIFF", AccountAction.SignIn, false), ("Clear the sample", AccountAction.ClearSample, true)],
            AccountScreen.Actions(En, AccountMode.Sample).Select(a => (a.Words, a.Action, a.Destructive)));
        Assert.Equal([("Sign out", AccountAction.SignOut, true)], AccountScreen.Actions(En, AccountMode.Kiff).Select(a => (a.Words, a.Action, a.Destructive)));
        Assert.Equal([AccountAction.SignIn], AccountScreen.Actions(En, AccountMode.SignedOut).Select(a => a.Action));
        var (title, body) = AccountScreen.SignOutQuestion(En);
        Assert.Equal("Sign out of KIFF?", title);
        Assert.Contains("Windows PC", body);
    }

    [Fact]
    public void Legal_rows_are_plain_where_kiff_dev_has_the_pages_in_the_app_s_language()
    {
        Assert.Equal("Terms", AccountScreen.Legal(En, "settings.terms"));
        var fr = WordBook.Load(SharedFiles.Strings, "fr");
        Assert.Equal(fr.Get("settings.terms"), AccountScreen.Legal(fr, "settings.terms"));
        Assert.Equal(fr.Get("settings.privacy_policy"), AccountScreen.Legal(fr, "settings.privacy_policy"));
    }

    [Theory]
    [InlineData("en", "English")]
    [InlineData("fr", "Français")]
    [InlineData("de", "Deutsch")]
    [InlineData("pt", "Português")]
    public void A_language_by_its_own_name(string language, string name) => Assert.Equal(name, AccountScreen.LanguageName(language));

    [Fact]
    public void The_version_as_the_iPad_shows_it()
    {
        Assert.Equal("1.0 (1)", AccountScreen.VersionLabel(new Version(1, 0, 1)));
        Assert.Equal("0.1", AccountScreen.VersionLabel(new Version(0, 1, 0)));
    }

    [Theory]
    [MemberData(nameof(Shipped))]
    public void Every_line_is_filled_in_every_shipped_language(string language)
    {
        var w = WordBook.Load(SharedFiles.Strings, language);
        var lines = new[] { AccountScreen.HowItWorks(w), AccountScreen.Legal(w, "settings.terms"), AccountScreen.Legal(w, "settings.privacy_policy"),
            AccountScreen.SignOutQuestion(w).Body, AccountScreen.LanguageName(w.Language) }
            .Concat(Enum.GetValues<AccountMode>().SelectMany(m => AccountScreen.Actions(w, m).Select(a => a.Words).Append(AccountScreen.ModeWords(w, m))));
        Assert.All(lines, l => Assert.DoesNotContain("{", l));
    }
}
