using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.Kiff;
using Tafs.Core.Tests.Kiff;

namespace Tafs.Core.Tests.Approvals;

/// <summary>
/// The Windows passkey, checked the way KIFF checks it (VerifyAssertion's steps) and against the
/// security review's checks: UV, the exact rpIdHash, an origin that's KIFF's, this computer's own
/// passkey (no BE or BS), and a challenge bound to one request.
/// </summary>
[Part("approvals")]
public class PasskeyApprovalTests
{
    const byte UP = 0x01, UV = 0x04, BE = 0x08, BS = 0x10;
    static readonly byte[] CredentialId = Encoding.UTF8.GetBytes("this-computer-credential");
    static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"card":"c1"}""");
    static readonly DateTimeOffset At = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    /// <summary>Stands in for webauthn.dll: signs authData ‖ sha256(clientDataJSON) as an authenticator does.</summary>
    sealed class FakeAuthenticator(TestSigner key, byte flags, string rpId = "kiff.dev") : IPasskeyAuthenticator
    {
        public byte[]? LastClientData;

        public async Task<PasskeyAssertion> GetAssertionAsync(string rp, byte[] credentialId, byte[] clientDataJson, CancellationToken cancel)
        {
            LastClientData = clientDataJson;
            var authData = SHA256.HashData(Encoding.UTF8.GetBytes(rpId)).Concat(new byte[] { flags, 0, 0, 0, 7 }).ToArray();
            var signed = authData.Concat(SHA256.HashData(clientDataJson)).ToArray();
            return new PasskeyAssertion(authData, await key.SignDerAsync(signed, cancel));
        }
    }

    static Task<string> Approve(IPasskeyAuthenticator auth, bool enabled = true) =>
        new PasskeyApprover(auth, CredentialId, enabled).ApproveAsync("t1", "POST", "/v1/cards", Body, At);

    [Fact]
    public async Task The_header_verifies_as_KIFF_verifies_it()
    {
        using var key = new TestSigner();
        var header = await Approve(new FakeAuthenticator(key, UP | UV));

        Assert.StartsWith("t=2026-11-14T20:00:00Z,a=", header);
        using var doc = JsonDocument.Parse(KiffSigning.FromBase64Url(header[(header.IndexOf(",a=") + 3)..]));
        var a = doc.RootElement;
        byte[] Field(string name) => KiffSigning.FromBase64Url(a.GetProperty(name).GetString()!);

        // VerifyAssertion, step by step.
        Assert.Equal(CredentialId, Field("credential_id"));
        var rawClient = Field("client_data_json");
        using var client = JsonDocument.Parse(rawClient);
        Assert.Equal("webauthn.get", client.RootElement.GetProperty("type").GetString());
        Assert.Equal("https://kiff.dev", client.RootElement.GetProperty("origin").GetString());
        Assert.False(client.RootElement.GetProperty("crossOrigin").GetBoolean());
        var authData = Field("authenticator_data");
        Assert.Equal(SHA256.HashData("kiff.dev"u8), authData[..32]);
        var message = KiffSigning.RequestV1("t1", "POST", "/v1/cards", Body, At);
        Assert.Equal(SHA256.HashData(message), KiffSigning.FromBase64Url(client.RootElement.GetProperty("challenge").GetString()!));
        Assert.True((authData[32] & UP) != 0 && (authData[32] & UV) != 0);
        var signed = authData.Concat(SHA256.HashData(rawClient)).ToArray();
        Assert.True(TestSigner.Verify(key.PublicKeyDer, signed, Field("signature")));
    }

    [Fact]
    public async Task The_challenge_binds_one_request_and_changes_with_it()
    {
        using var key = new TestSigner();
        var auth = new FakeAuthenticator(key, UP | UV);
        var approver = new PasskeyApprover(auth, CredentialId, true);
        await approver.ApproveAsync("t1", "POST", "/v1/cards", Body, At);
        var first = auth.LastClientData!;
        await approver.ApproveAsync("t1", "POST", "/v1/cards", Body, At.AddSeconds(1));
        Assert.NotEqual(first, auth.LastClientData);
        await approver.ApproveAsync("t1", "POST", "/v1/cards", Encoding.UTF8.GetBytes("{}"), At);
        Assert.NotEqual(first, auth.LastClientData);
    }

    [Theory]
    [InlineData(UP, "didn't verify")]               // presence without Windows Hello
    [InlineData(UV, "didn't verify")]               // no presence flag
    [InlineData(UP | UV | BE, "can leave")]          // backup eligible: could sync off this computer
    [InlineData(UP | UV | BS, "can leave")]          // backed up: already synced
    public async Task What_KIFF_would_refuse_is_never_sent(byte flags, string reason)
    {
        using var key = new TestSigner();
        var e = await Assert.ThrowsAsync<PasskeyRefusedException>(() => Approve(new FakeAuthenticator(key, flags)));
        Assert.Contains(reason, e.Message);
    }

    [Fact]
    public async Task Another_relying_party_s_assertion_is_never_sent()
    {
        using var key = new TestSigner();
        var e = await Assert.ThrowsAsync<PasskeyRefusedException>(() => Approve(new FakeAuthenticator(key, UP | UV, "evil.example")));
        Assert.Contains("kiff.dev", e.Message);
    }

    [Fact]
    public void It_is_on_now_that_KIFF_accepts_a_computer_s_own_passkey() =>
        Assert.True(Features.WindowsPasskeyApprovals);

    [Fact]
    public async Task Switched_off_Windows_Hello_is_never_asked()
    {
        using var key = new TestSigner();
        var auth = new FakeAuthenticator(key, UP | UV);
        await Assert.ThrowsAsync<PasskeyRefusedException>(() => Approve(auth, enabled: false));
        Assert.Null(auth.LastClientData); // Windows Hello is never even asked
    }

    /// <summary>
    /// A step confirmed with Windows Hello (planque's setup): one assertion over sha256 of its message, checked as an
    /// approval's is, so Windows Hello not verifying the person, or a passkey that can leave this PC, stops it.
    /// </summary>
    [Fact]
    public async Task A_step_is_confirmed_with_this_PC_s_passkey()
    {
        using var key = new TestSigner();
        var message = Encoding.UTF8.GetBytes("tafs.planque.v1|t1");
        var auth = new FakeAuthenticator(key, UP | UV);

        await new PasskeyApprover(auth, CredentialId, enabled: true).ConfirmAsync(message);

        using var clientData = JsonDocument.Parse(auth.LastClientData!);
        Assert.Equal(KiffSigning.Base64Url(SHA256.HashData(message)), clientData.RootElement.GetProperty("challenge").GetString());
        await Assert.ThrowsAsync<PasskeyRefusedException>(() => new PasskeyApprover(new FakeAuthenticator(key, UP), CredentialId, true).ConfirmAsync(message));
        await Assert.ThrowsAsync<PasskeyRefusedException>(() => new PasskeyApprover(new FakeAuthenticator(key, UP | UV | BE), CredentialId, true).ConfirmAsync(message));
    }
}
