using System.Text;
using Tafs.Core.Account;
using Tafs.Core.Kiff;
using Tafs.Core.NeedsYou;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Account;

/// <summary>The account's time zone set from this PC, as the iPad's setAccountTimeZone: Windows Hello, then the device key's signature.</summary>
[Part("account")]
public class AccountTimeZoneTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    sealed class Calls
    {
        public readonly List<string> Made = [];
        public StepConfirmed Confirmed = StepConfirmed.Yes;
        public (int, string) Answer = (200, """{"time_zone":"Europe/Paris"}""");
        public bool Current = true;
        public byte[]? Signed;

        public Task<(string? Zone, string? Why)> Set(string zone = "Europe/Paris") => AccountTimeZone.SetAsync(En, "t_01", zone,
            prompt => { Made.Add("confirm " + prompt); return Task.FromResult(Confirmed); },
            message => { Made.Add("sign"); Signed = message; return Task.FromResult(new byte[] { 1, 2 }); },
            header => { Made.Add("put " + header); return Task.FromResult(Answer); },
            () => "2026-11-16T12:00:00.123Z", () => Current);
    }

    /// <summary>Windows Hello with the iPad's prompt, then the signature over exactly the PUT, then the PUT with its header.</summary>
    [Fact]
    public async Task The_zone_is_confirmed_then_signed_over_its_exact_request()
    {
        var calls = new Calls();
        Assert.Equal(("Europe/Paris", null), await calls.Set());
        Assert.Equal(["confirm Set your account's time zone to Europe/Paris", "sign", "put t=2026-11-16T12:00:00.123Z,v1=AQI="], calls.Made);
        Assert.Equal(KiffSigning.RequestV1("t_01", "PUT", "/v1/me/time-zone", AccountTimeZone.Body("Europe/Paris"), "2026-11-16T12:00:00.123Z"), calls.Signed);
        Assert.Equal("""{"time_zone":"Europe/Paris"}""", Encoding.UTF8.GetString(AccountTimeZone.Body("Europe/Paris")));
    }

    /// <summary>Closed or failing, nothing is signed or sent; KIFF refusing says the app's line; a sign-out sends nothing more.</summary>
    [Fact]
    public async Task Anything_but_a_yes_sends_nothing_and_says_why()
    {
        var closed = new Calls { Confirmed = StepConfirmed.Cancelled };
        Assert.Equal((null, "The time zone didn't change."), await closed.Set());
        Assert.Single(closed.Made);

        var failed = new Calls { Confirmed = StepConfirmed.Failed };
        Assert.Equal((null, $"The time zone didn't change. {En.Get("common.not_sent_unconfirmed")}"), await failed.Set());
        Assert.Single(failed.Made);

        var refused = new Calls { Answer = (409, """{"error":"x"}""") };
        Assert.Equal((null, $"The time zone didn't change. {En.Get("error.changed")}"), await refused.Set());

        var signedOut = new Calls();
        signedOut.Current = false;
        Assert.Equal((null, null), await signedOut.Set());
        Assert.Single(signedOut.Made);
    }

    /// <summary>
    /// Offered only where KIFF takes the PUT: <c>account.settings</c>, or no list at all; never on a view-only session, which
    /// would get Windows Hello and then KIFF's refusal (kiff-qa on #97).
    /// </summary>
    [Fact]
    public void The_zone_is_offered_with_account_settings_and_never_view_only()
    {
        Assert.True(new KiffAccount("t", "s", null, null, null, ["account.settings"]).Offered(viewOnly: false).TimeZone);
        Assert.False(new KiffAccount("t", "s", null, null, null, ["cards.manage", "account.delete"]).Offered(viewOnly: false).TimeZone);
        Assert.True(new KiffAccount("t", "s", null, null, null).Offered(viewOnly: false).TimeZone);
        Assert.False(new KiffAccount("t", "s", null, null, null, ["account.settings"]).Offered(viewOnly: true).TimeZone);
        Assert.False(new KiffAccount("t", "s", null, null, null).Offered(viewOnly: true).TimeZone);
    }

    [Fact]
    public void This_PC_s_zone_is_its_IANA_name() =>
        Assert.Equal("Europe/Paris", AccountTimeZone.ThisPc(TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Romance Standard Time" : "Europe/Paris")));
}
