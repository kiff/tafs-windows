using System.Text;
using Tafs.Core.Taf;

namespace Tafs.Core.Tests.Taf;

/// <summary>
/// Adding a WhatsApp door on Windows: the sign-up page with a one-use state, and only the return carrying it, for that taf,
/// within 15 minutes (the PM's (a), kiff/work#20).
/// </summary>
[Part("record")]
public class WhatsAppDoorTests
{
    static readonly DateTimeOffset Opened = new(2026, 11, 23, 0, 10, 0, TimeSpan.Zero);

    [Fact]
    public void Each_sign_up_has_its_own_state()
    {
        var a = WhatsAppDoor.NewState();
        var b = WhatsAppDoor.NewState();
        Assert.NotEqual(a, b);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", a);
        Assert.Equal("https://cloud.tafs.kiff.dev/doors/whatsapp/connect?taf=t%2F1&state=" + a,
            WhatsAppDoor.SignupPage(new Uri("https://cloud.tafs.kiff.dev"), "t/1", a).AbsoluteUri);
    }

    /// <summary>The page's return as it writes it: URLSearchParams, so spaces as "+".</summary>
    [Fact]
    public void The_pages_return_is_read()
    {
        var back = WhatsAppDoor.Read(new Uri("tafs://doors/whatsapp?taf=t1&state=s_1&code=AQ%2Bx+y&phone_number_id=106&waba_id=2003"))!;
        Assert.Equal(new WhatsAppSignedUp("t1", "s_1", "AQ+x y", "106", "2003"), back);
        // Meta gave no ids: they come back empty, and the door is still asked for.
        Assert.Equal(new WhatsAppSignedUp("t1", "s_1", "c", "", ""),
            WhatsAppDoor.Read(new Uri("tafs://doors/whatsapp?taf=t1&state=s_1&code=c&phone_number_id=&waba_id=")));
    }

    [Theory]
    [InlineData("tafs://doors/whatsapp?taf=t1&code=c")]
    [InlineData("tafs://doors/whatsapp?state=s&code=c")]
    [InlineData("tafs://doors/whatsapp?taf=t1&state=s")]
    [InlineData("tafs://doors/whatsapp?taf=t1&state=s&code=")]
    [InlineData("tafs://doors/whatsapp?taf=t1&state=s&code=c&code=d")]
    [InlineData("tafs://doors/slack?taf=t1&state=s&code=c")]
    [InlineData("tafs://planque/signed-in?taf=t1&state=s&code=c")]
    [InlineData("https://doors/whatsapp?taf=t1&state=s&code=c")]
    public void Anything_else_is_no_return(string url) => Assert.Null(WhatsAppDoor.Read(new Uri(url)));

    /// <summary>Only this attempt's: the same taf, the same state, within 15 minutes of opening the page.</summary>
    [Fact]
    public void Only_the_return_with_this_attempts_state_for_this_taf_in_time_is_taken()
    {
        var back = new WhatsAppSignedUp("t1", "state-A", "c", "106", "2003");
        Assert.True(WhatsAppDoor.Takes(back, "t1", "state-A", Opened, Opened.AddMinutes(3)));
        Assert.True(WhatsAppDoor.Takes(back, "t1", "state-A", Opened, Opened.AddMinutes(15)));

        Assert.False(WhatsAppDoor.Takes(back, "t1", "state-B", Opened, Opened.AddMinutes(3)), "another state: a link that didn't come from this sign-up");
        Assert.False(WhatsAppDoor.Takes(back, "t1", "state-", Opened, Opened.AddMinutes(3)));
        Assert.False(WhatsAppDoor.Takes(back, "t2", "state-A", Opened, Opened.AddMinutes(3)), "another taf");
        Assert.False(WhatsAppDoor.Takes(back, "t1", "state-A", Opened, Opened.AddMinutes(15).AddSeconds(1)), "too late");
        Assert.False(WhatsAppDoor.Takes(back, "t1", "state-A", Opened, Opened.AddSeconds(-1)), "before the page was opened");
    }

    [Fact]
    public void The_door_is_made_with_metas_code_and_the_numbers_ids()
    {
        var body = Encoding.UTF8.GetString(WhatsAppDoor.DoorBody("whatsapp", new WhatsAppSignedUp("t1", "s", "AQ+x", "106", "2003")));
        var sent = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(body)!;
        Assert.Equal(new Dictionary<string, string> { ["app"] = "whatsapp", ["code"] = "AQ+x", ["phone_number_id"] = "106", ["waba_id"] = "2003" }, sent);
        // The state stays on this PC: tafs cloud's door doesn't need it.
        Assert.DoesNotContain("state", body);
    }
}
