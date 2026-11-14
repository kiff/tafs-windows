using System.Text.Json;
using Tafs.Core.SignIn;
using Tafs.Core.Tests.Kiff;

namespace Tafs.Core.Tests.SignIn;

[Part("pairing")]
public class DeviceSignInTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 14, 22, 0, 0, TimeSpan.Zero);

    const string Start = """
        {"device_code":"dc-secret","user_code":"ABCD-EFGH","verification_uri":"https://kiff.dev/device",
         "verification_uri_complete":"https://kiff.dev/device?code=ABCD-EFGH","expires_in":600,"interval":5}
        """;

    static SignInCode Code() => DeviceSignIn.ReadStart(200, Start, Now)!;

    [Fact]
    public void The_start_sends_both_keys_in_standard_base64_as_a_windows_computer()
    {
        using var device = new TestSigner();
        using var approval = new TestSigner();
        using var body = JsonDocument.Parse(DeviceSignIn.StartBody(device.PublicKeyDer, approval.PublicKeyDer, "tafs on this Windows PC"));
        var r = body.RootElement;

        Assert.Equal(new[] { "public_key", "approval_public_key", "device_name", "platform", "kind" },
            r.EnumerateObject().Select(p => p.Name));
        Assert.Equal(device.PublicKeyDer, Convert.FromBase64String(r.GetProperty("public_key").GetString()!));
        Assert.Equal(approval.PublicKeyDer, Convert.FromBase64String(r.GetProperty("approval_public_key").GetString()!));
        Assert.Equal("windows", r.GetProperty("platform").GetString());
        Assert.Equal("other", r.GetProperty("kind").GetString());
    }

    [Fact]
    public void With_no_approval_key_it_asks_for_a_passkey_only_session()
    {
        using var device = new TestSigner();
        using var body = JsonDocument.Parse(DeviceSignIn.StartBody(device.PublicKeyDer, null, "tafs on this Windows PC", deviceKeyInSoftware: true));
        var r = body.RootElement;
        Assert.Equal("passkey", r.GetProperty("approval").GetString());
        Assert.Equal("software", r.GetProperty("key_store").GetString());
        Assert.False(r.TryGetProperty("approval_public_key", out _));
    }

    [Fact]
    public void The_start_keeps_KIFF_s_key_rules()
    {
        using var device = new TestSigner();
        Assert.Throws<ArgumentException>(() => DeviceSignIn.StartBody(device.PublicKeyDer, device.PublicKeyDer, "x"));
        Assert.Throws<ArgumentException>(() => DeviceSignIn.StartBody(device.PublicKeyDer, null, new string('a', 65)));
    }

    [Fact]
    public void The_answer_gives_the_code_the_link_and_when_it_expires()
    {
        var code = Code();
        Assert.Equal("ABCD-EFGH", code.UserCode);
        Assert.Equal("https://kiff.dev/device?code=ABCD-EFGH", code.VerificationUriComplete.AbsoluteUri);
        Assert.Equal(Now.AddMinutes(10), code.Expires);
        Assert.Equal(TimeSpan.FromSeconds(5), code.Interval);
        Assert.DoesNotContain("dc-secret", code.ToString());
    }

    [Theory]
    [InlineData("http://kiff.dev/device")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:privacy")]
    public void A_link_that_isn_t_https_is_never_opened(string link) =>
        Assert.Null(DeviceSignIn.ReadStart(200, Start.Replace("https://kiff.dev/device?code=ABCD-EFGH", link), Now));

    [Theory]
    [InlineData(400, """{"error":"invalid_request"}""")]
    [InlineData(200, """{"user_code":"ABCD"}""")]
    [InlineData(200, "not json")]
    public void An_answer_that_can_t_be_used_is_null(int status, string body) =>
        Assert.Null(DeviceSignIn.ReadStart(status, body, Now));

    [Fact]
    public void A_short_interval_is_raised_to_five_seconds() =>
        Assert.Equal(TimeSpan.FromSeconds(5), DeviceSignIn.ReadStart(200, Start.Replace("\"interval\":5", "\"interval\":1"), Now)!.Interval);

    [Theory]
    [InlineData(400, """{"error":"authorization_pending"}""", SignInStatus.Pending)]
    [InlineData(400, """{"error":"slow_down"}""", SignInStatus.SlowDown)]
    [InlineData(400, """{"error":"expired_token"}""", SignInStatus.Expired)]
    [InlineData(403, """{"error":"access_denied"}""", SignInStatus.Failed)]
    public void Each_token_answer_is_read(int status, string body, SignInStatus expected) =>
        Assert.Equal(expected, DeviceSignIn.ReadToken(status, body).Status);

    [Fact]
    public void The_session_is_handed_over_and_its_token_never_printed()
    {
        var result = DeviceSignIn.ReadToken(200,
            """{"access_token":"tok-secret","token_type":"Bearer","expires_in":3600,"tenant":"t1","subject":"s1"}""");
        Assert.Equal(SignInStatus.SignedIn, result.Status);
        Assert.Equal("tok-secret", result.Session!.AccessToken);
        Assert.DoesNotContain("tok-secret", result.Session.ToString());
        Assert.DoesNotContain("tok-secret", result.ToString());
    }

    [Fact]
    public void An_unreadable_200_is_never_echoed()
    {
        var result = DeviceSignIn.ReadToken(200, """{"access_token":"tok-secret"}""");
        Assert.Equal(SignInStatus.Failed, result.Status);
        Assert.DoesNotContain("tok-secret", result.Detail);
    }

    [Fact]
    public async Task It_polls_at_KIFF_s_interval_slows_down_and_stops_signed_in()
    {
        var answers = new Queue<(int, string)>(new[]
        {
            (400, """{"error":"authorization_pending"}"""),
            (400, """{"error":"slow_down"}"""),
            (400, """{"error":"authorization_pending"}"""),
            (200, """{"access_token":"a","expires_in":3600,"tenant":"t","subject":"s"}"""),
        });
        var waits = new List<TimeSpan>();
        var sent = new List<string>();
        var result = await DeviceSignIn.PollAsync(Code(),
            (body, _) => { sent.Add(System.Text.Encoding.UTF8.GetString(body)); return Task.FromResult(answers.Dequeue()); },
            () => Now, (d, _) => { waits.Add(d); return Task.CompletedTask; });

        Assert.Equal(SignInStatus.SignedIn, result.Status);
        Assert.Equal(new[] { 5, 5, 10, 10 }, waits.Select(w => (int)w.TotalSeconds));
        Assert.All(sent, b => Assert.Equal("""{"device_code":"dc-secret"}""", b));
    }

    [Fact]
    public async Task It_stops_once_the_code_expires_without_asking_again()
    {
        var asked = 0;
        var result = await DeviceSignIn.PollAsync(Code(),
            (_, _) => { asked++; return Task.FromResult((400, """{"error":"authorization_pending"}""")); },
            () => Now.AddMinutes(11), (_, _) => Task.CompletedTask);
        Assert.Equal(SignInStatus.Expired, result.Status);
        Assert.Equal(0, asked);
    }
}
