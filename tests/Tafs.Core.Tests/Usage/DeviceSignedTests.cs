using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.Kiff;
using Tafs.Core.Tests.Kiff;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Usage;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Usage;

/// <summary>
/// Raise the limit and Decline, the two acts KIFF takes only signed by the device key: Windows Hello's passkey asked fresh over
/// that very request, its answer checked here, then the device key, then KIFF, in that order, and nothing without each step.
/// </summary>
[Part("calendar")]
public class DeviceSignedTests
{
    const byte UP = 0x01, UV = 0x04, BE = 0x08, BS = 0x10;
    const string At = "2026-11-24T05:40:00.123Z";
    static readonly DateTimeOffset Now = new(2026, 11, 24, 5, 40, 5, TimeSpan.Zero);
    static readonly byte[] Credential = Encoding.UTF8.GetBytes("this-pc-passkey");
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    /// <summary>Stands in for webauthn.dll: signs authData ‖ sha256(clientDataJSON) with the passkey, and records that it was asked.</summary>
    sealed class FakeHello(TestSigner passkey, List<string> calls, byte flags = UP | UV, string rpId = "kiff.dev") : IPasskeyAuthenticator
    {
        public byte[]? Answering { get; init; }
        public byte[]? OverClientData { get; init; }
        public bool Closes { get; init; }
        public byte[]? AskedFor;

        public async Task<PasskeyAssertion> GetAssertionAsync(string rp, byte[] credentialId, byte[] clientDataJson, CancellationToken cancel)
        {
            calls.Add("hello");
            AskedFor = credentialId;
            if (Closes) throw new OperationCanceledException();
            var authData = SHA256.HashData(Encoding.UTF8.GetBytes(rpId)).Concat(new byte[] { flags, 0, 0, 0, 9 }).ToArray();
            var signed = authData.Concat(SHA256.HashData(OverClientData ?? clientDataJson)).ToArray();
            return new PasskeyAssertion(authData, await passkey.SignDerAsync(signed, cancel)) { CredentialId = Answering };
        }
    }

    static DeviceSignedRequest Raise(string at = At, long from = 20_000) => DeviceSignedRequest.Raise("t_acme", from, at)!;

    sealed class Run : IDisposable
    {
        public readonly TestSigner Passkey = new(), Device = new();
        public readonly List<string> Calls = [];
        public readonly List<(DeviceSignedRequest Request, string Header)> Sent = [];
        public bool Current = true;

        public Task<DeviceSent> SendAsync(DeviceSignedRequest r, IPasskeyAuthenticator hello, byte[]? kept = null, DateTimeOffset? now = null, int status = 200, string body = "{}") =>
            DeviceSigning.SendAsync(r, hello, Credential, kept ?? Passkey.PublicKeyDer,
                async m => { Calls.Add("sign"); return await Device.SignDerAsync(m); },
                (req, header) => { Calls.Add("send"); Sent.Add((req, header)); return Task.FromResult((status, body)); },
                () => now ?? Now, () => Current);

        public void Dispose() { Passkey.Dispose(); Device.Dispose(); }
    }

    [Fact]
    public void Raise_and_Decline_are_KIFF_s_own_requests_fixed_when_made()
    {
        var r = Raise();
        Assert.Equal(("PUT", "/v1/me/usage/limit"), (r.Method, r.Path));
        Assert.Equal("""{"raise":true,"from_cents":20000}""", Encoding.UTF8.GetString(r.Body));
        Assert.Equal(KiffSigning.RequestV1("t_acme", "PUT", "/v1/me/usage/limit", r.Body, At), r.Message);
        // The body handed out is a copy: changing it changes nothing that's signed.
        r.Body[0] = (byte)'X';
        Assert.Equal("""{"raise":true,"from_cents":20000}""", Encoding.UTF8.GetString(r.Body));

        var d = DeviceSignedRequest.Decline("t_acme", "q/1", " Not this month ", At)!;
        Assert.Equal(("POST", "/v1/me/pack-requests/q%2F1/decline"), (d.Method, d.Path));
        Assert.Equal("""{"note":"Not this month"}""", Encoding.UTF8.GetString(d.Body));

        Assert.Null(DeviceSignedRequest.Raise("", 1, At));
        Assert.Null(DeviceSignedRequest.Raise("t_acme", -1, At));
        Assert.Null(DeviceSignedRequest.Decline("t_acme", "", "", At));
    }

    /// <summary>What the passkey answers over is never a message KIFF would take: its own first line, and the request's hash.</summary>
    [Fact]
    public void The_presence_message_is_never_KIFF_s()
    {
        var r = Raise();
        var lines = Encoding.UTF8.GetString(r.PresenceMessage).Split('\n');
        Assert.Equal(["tafs.presence.request.v1", "t_acme", At, KiffSigning.Sha256Hex(r.Message)], lines);
        Assert.DoesNotContain("kiff.", lines[0]);
        Assert.NotEqual(PasskeyApprover.Challenge(r.Message), PasskeyApprover.Challenge(r.PresenceMessage));
    }

    [Fact]
    public async Task Windows_Hello_then_the_device_key_then_KIFF_once()
    {
        using var run = new Run();
        var hello = new FakeHello(run.Passkey, run.Calls);
        var r = Raise();
        var sent = await run.SendAsync(r, hello);
        Assert.Equal(DeviceSentKind.Sent, sent.Kind);
        Assert.Equal(["hello", "sign", "send"], run.Calls);
        Assert.Equal(Credential, hello.AskedFor);
        var (request, header) = Assert.Single(run.Sent);
        Assert.Same(r, request);
        Assert.StartsWith("t=" + At + ",v1=", header);
        Assert.True(TestSigner.Verify(run.Device.PublicKeyDer, r.Message, Convert.FromBase64String(header[("t=" + At + ",v1=").Length..])));
        Assert.Null(DeviceSigning.RaiseSaid(En, sent));
    }

    /// <summary>The answer Windows names as the one asked, or names none (then it's the one asked): both confirm.</summary>
    [Fact]
    public async Task An_answer_naming_the_passkey_asked_or_none_confirms()
    {
        using var run = new Run();
        Assert.Equal(DeviceSentKind.Sent, (await run.SendAsync(Raise(), new FakeHello(run.Passkey, run.Calls) { Answering = Credential })).Kind);
    }

    /// <summary>Each refusal sends nothing, and signs nothing: the device key is never asked before a checked presence.</summary>
    [Fact]
    public async Task Nothing_is_signed_or_sent_without_a_checked_presence()
    {
        using var run = new Run();
        using var other = new TestSigner();
        var refused = new (string Why, Func<FakeHello> Hello, byte[]? Kept)[]
        {
            ("no verification", () => new FakeHello(run.Passkey, run.Calls, UP), null),
            ("no presence", () => new FakeHello(run.Passkey, run.Calls, UV), null),
            ("a synced passkey (BE)", () => new FakeHello(run.Passkey, run.Calls, UP | UV | BE), null),
            ("a synced passkey (BS)", () => new FakeHello(run.Passkey, run.Calls, UP | UV | BE | BS), null),
            ("another relying party", () => new FakeHello(run.Passkey, run.Calls, rpId: "evil.example"), null),
            ("another credential", () => new FakeHello(run.Passkey, run.Calls) { Answering = [1, 2, 3] }, null),
            ("another key", () => new FakeHello(other, run.Calls), null),
            ("the key kept isn't the passkey's", () => new FakeHello(run.Passkey, run.Calls), other.PublicKeyDer),
            ("a key kept that isn't a key", () => new FakeHello(run.Passkey, run.Calls), [1, 2, 3]),
            ("an answer over another request", () => new FakeHello(run.Passkey, run.Calls) { OverClientData = PasskeyApprover.ClientDataJson(Raise(from: 1).PresenceMessage) }, null),
            ("an answer over KIFF's own message", () => new FakeHello(run.Passkey, run.Calls) { OverClientData = PasskeyApprover.ClientDataJson(Raise().Message) }, null),
        };
        foreach (var (why, hello, kept) in refused)
        {
            run.Calls.Clear();
            var sent = await run.SendAsync(Raise(), hello(), kept);
            Assert.True(sent.Kind == DeviceSentKind.Unconfirmed, why);
            Assert.True(run.Calls.SequenceEqual(["hello"]), why + ": " + string.Join(",", run.Calls));
            Assert.Equal(En.Get("common.not_sent_unconfirmed"), DeviceSigning.RaiseSaid(En, sent));
        }
        Assert.Empty(run.Sent);
    }

    [Fact]
    public async Task Closing_Windows_Hello_sends_nothing_and_says_nothing()
    {
        using var run = new Run();
        var sent = await run.SendAsync(Raise(), new FakeHello(run.Passkey, run.Calls) { Closes = true });
        Assert.Equal(DeviceSentKind.Cancelled, sent.Kind);
        Assert.Equal(["hello"], run.Calls);
        Assert.Null(DeviceSigning.RaiseSaid(En, sent));
        Assert.Null(DeviceSigning.DeclineSaid(En, sent));
    }

    [Fact]
    public async Task A_sign_out_during_Windows_Hello_stops_it_quietly()
    {
        using var run = new Run();
        var hello = new FakeHello(run.Passkey, run.Calls);
        var sent = await DeviceSigning.SendAsync(Raise(), hello, Credential, run.Passkey.PublicKeyDer,
            m => { run.Calls.Add("sign"); return run.Device.SignDerAsync(m); },
            (_, _) => { run.Calls.Add("send"); return Task.FromResult((200, "{}")); },
            () => Now, () => !run.Calls.Contains("hello"));
        Assert.Equal(DeviceSentKind.Stopped, sent.Kind);
        Assert.Equal(["hello"], run.Calls);
    }

    /// <summary>A presence left too long (KIFF's window for signed_at) signs nothing: the person confirms again.</summary>
    [Fact]
    public async Task A_stale_confirmation_signs_nothing()
    {
        using var run = new Run();
        var sent = await run.SendAsync(Raise(), new FakeHello(run.Passkey, run.Calls), now: Now + Presence.Life + TimeSpan.FromSeconds(1));
        Assert.Equal(DeviceSentKind.Unconfirmed, sent.Kind);
        Assert.Equal(["hello"], run.Calls);
    }

    /// <summary>A presence signs its own request, once; another request is refused and spends it.</summary>
    [Fact]
    public async Task A_presence_signs_its_own_request_once()
    {
        using var passkey = new TestSigner();
        using var device = new TestSigner();
        var hello = new FakeHello(passkey, []);
        async Task<Presence> Checked(DeviceSignedRequest r) =>
            Presence.Check(r, await hello.GetAssertionAsync(PasskeyApprover.RpId, Credential, PasskeyApprover.ClientDataJson(r.PresenceMessage), default), Credential, passkey.PublicKeyDer);

        var r = Raise();
        var p = await Checked(r);
        Assert.StartsWith("t=" + At, await p.SignAsync(r, m => device.SignDerAsync(m), Now));
        await Assert.ThrowsAsync<PasskeyRefusedException>(() => p.SignAsync(r, m => device.SignDerAsync(m), Now));

        foreach (var another in new[] { Raise(from: 1), Raise("2026-11-24T05:40:01.000Z"), DeviceSignedRequest.Raise("t_other", 20_000, At)!, DeviceSignedRequest.Decline("t_acme", "q1", "", At)! })
        {
            var q = await Checked(r);
            await Assert.ThrowsAsync<PasskeyRefusedException>(() => q.SignAsync(another, m => device.SignDerAsync(m), Now));
            // Spent on the mismatch: it never signs its own after.
            await Assert.ThrowsAsync<PasskeyRefusedException>(() => q.SignAsync(r, m => device.SignDerAsync(m), Now));
        }
    }

    [Theory]
    [InlineData(409, """{"error":"limit_changed","limit_cents":13000}""", "usage.limit_changed")]
    [InlineData(409, """{"error":"limit_ceiling"}""", "usage.limit_ceiling")]
    public void KIFF_s_409s_in_the_app_s_words(int status, string body, string key)
    {
        Assert.Equal(En.Get(key), DeviceSigning.RaiseSaid(En, new DeviceSent(DeviceSentKind.Sent, status, body)));
        // A decline's 409 is only that it wasn't declined.
        Assert.Equal(En.Get("billing.request.not_declined", ("why", KiffErrors.Words(En, status))), DeviceSigning.DeclineSaid(En, new DeviceSent(DeviceSentKind.Sent, status, body)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(403)]
    [InlineData(409)]
    [InlineData(500)]
    public void Not_raised_says_why(int status) =>
        Assert.Equal(En.Get("usage.raise_limit.not_raised", ("why", KiffErrors.Words(En, status))),
            DeviceSigning.RaiseSaid(En, new DeviceSent(DeviceSentKind.Sent, status, "not json")));

    /// <summary>Raise's sheet: tafs cloud's offer, then the signed limit in the currency tafs cloud names, at its own decimals.</summary>
    [Fact]
    public void The_sheet_shows_the_limit_raised_from_in_its_own_currency()
    {
        static RaiseOffer Offer(string raise) => RaiseOffer.Read(JsonDocument.Parse("{\"raise\":" + raise + "}").RootElement)!;
        var eur = Offer("""{"words":"Raise the limit to €300","from_cents":20000,"currency":"EUR"}""");
        Assert.Equal(("Raise the limit to €300", "The spend limit is €200 now."), eur.Lines(En));

        Assert.Equal("The spend limit is $200 now.", Offer("""{"words":"x","from_cents":20000,"currency":"USD"}""").Lines(En)!.Value.From);
        var yen = Offer("""{"words":"x","from_cents":20000,"currency":"JPY"}""").Lines(En)!.Value.From;
        Assert.Contains("20,000", yen);
        Assert.DoesNotContain("200.", yen);
        Assert.Contains("20.500", Offer("""{"words":"x","from_cents":20500,"currency":"KWD"}""").Lines(En)!.Value.From);
        // USD in French is never written in euros: the code's own symbol, the language's own digits.
        var fr = WordBook.Load(SharedFiles.Strings, "fr");
        var usdFr = Offer("""{"words":"x","from_cents":20050,"currency":"USD"}""").Lines(fr)!.Value.From;
        Assert.DoesNotContain("€", usdFr);
        Assert.Contains("200,50", usdFr);

        // No limit, no currency, or a code that isn't one: nothing to raise, never a guessed currency.
        Assert.Null(Offer("""{"words":"x","currency":"EUR"}""").Lines(En));
        Assert.Null(Offer("""{"words":"x","from_cents":20000}""").Lines(En));
        Assert.Null(Offer("""{"words":"x","from_cents":20000,"currency":"eur"}""").Lines(En));
        Assert.Null(Offer("""{"words":"x","from_cents":-5,"currency":"EUR"}""").Lines(En));
        Assert.Null(RaiseOffer.Read(JsonDocument.Parse("""{"raise":{"from_cents":1}}""").RootElement));
    }

    [Theory]
    [InlineData("JPY", 0)]
    [InlineData("KRW", 0)]
    [InlineData("CLP", 0)]
    [InlineData("ISK", 0)]
    [InlineData("VND", 0)]
    [InlineData("KWD", 3)]
    [InlineData("BHD", 3)]
    [InlineData("TND", 3)]
    [InlineData("CLF", 4)]
    [InlineData("EUR", 2)]
    [InlineData("USD", 2)]
    public void Each_currency_at_its_own_decimals(string code, int digits) => Assert.Equal(digits, Currencies.MinorDigits(code));
}
