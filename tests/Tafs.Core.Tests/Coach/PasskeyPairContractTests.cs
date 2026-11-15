using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.Coach;
using Tafs.Core.Kiff;
using Tafs.Core.Tests.Kiff;

namespace Tafs.Core.Tests.Coach;

/// <summary>
/// The pair's two <c>KIFF-Passkey-Assertion</c> headers, checked the way KIFF checks each request (its API's
/// the passkey form of the pair's <c>v3</c> header): <c>t=</c> first, then <c>a=</c>
/// and <c>with=sha256:</c> with 64 lowercase hex, no other part; the request's own <c>kiff.request.v1</c> rebuilt from its
/// method, path, body and <c>t</c>; a <c>with</c> that isn't its own hash; and the one assertion's challenge
/// base64url(sha256) of <c>kiff.requests.v1</c> over both hashes, sorted.
/// </summary>
[Part("approvals")]
public class PasskeyPairContractTests
{
    const byte UP = 0x01, UV = 0x04;
    const string Tenant = "01exampletenant0000000000";
    const string At = "2026-11-12T18:00:00.250Z";
    static readonly byte[] CredentialId = Encoding.UTF8.GetBytes("this-computer-credential");

    sealed class FakeAuthenticator(TestSigner key) : IPasskeyAuthenticator
    {
        public int Asked;

        public async Task<PasskeyAssertion> GetAssertionAsync(string rp, byte[] credentialId, byte[] clientDataJson, CancellationToken cancel)
        {
            Asked++;
            var authData = SHA256.HashData(Encoding.UTF8.GetBytes(rp)).Concat(new byte[] { UP | UV, 0, 0, 0, 1 }).ToArray();
            return new PasskeyAssertion(authData, await key.SignDerAsync(authData.Concat(SHA256.HashData(clientDataJson)).ToArray(), cancel));
        }
    }

    static StartRequest Request(string id, string path, bool gateway, string body) =>
        new(id, "POST", path, gateway, Encoding.UTF8.GetBytes(body), [], null, []);

    static string Hex(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    /// <summary>KIFF's requestMessage: the request alone, as its service rebuilds it.</summary>
    static byte[] KiffRequest(string method, string uri, byte[] body, string at) =>
        Encoding.UTF8.GetBytes(string.Join("\n", "kiff.request.v1", Tenant, method, uri, Hex(body), at));

    /// <summary>KIFF's RequestsMessage: the two hashes in sorted order.</summary>
    static byte[] KiffRequests(string at, string one, string other)
    {
        var (a, b) = string.CompareOrdinal(other, one) < 0 ? (other, one) : (one, other);
        return Encoding.UTF8.GetBytes(string.Join("\n", "kiff.requests.v1", Tenant, at, a, b));
    }

    /// <summary>KIFF's check of one request, given its header, done step by step; returns the assertion's <c>a=</c>.</summary>
    static string Verify(TestSigner key, StartRequest request, string header)
    {
        var parts = header.Split(',');
        Assert.Equal(3, parts.Length);
        Assert.StartsWith("t=", parts[0]);
        var at = parts[0][2..];
        Assert.Equal(At, at);
        var a = Assert.Single(parts[1..], p => p.StartsWith("a=", StringComparison.Ordinal))[2..];
        var with = Assert.Single(parts[1..], p => p.StartsWith("with=", StringComparison.Ordinal))[5..];
        Assert.StartsWith("sha256:", with);
        var other = with["sha256:".Length..];
        Assert.Matches("^[0-9a-f]{64}$", other);

        var own = Hex(KiffRequest(request.Method, request.Path, request.Body, at));
        Assert.NotEqual(own, other);
        var message = KiffRequests(at, own, other);

        Assert.DoesNotContain('=', a);
        using var doc = JsonDocument.Parse(KiffSigning.FromBase64Url(a));
        byte[] Field(string name) => KiffSigning.FromBase64Url(doc.RootElement.GetProperty(name).GetString()!);
        Assert.Equal(CredentialId, Field("credential_id"));
        var client = Field("client_data_json");
        using var clientDoc = JsonDocument.Parse(client);
        Assert.Equal(KiffSigning.Base64Url(SHA256.HashData(message)), clientDoc.RootElement.GetProperty("challenge").GetString());
        var authData = Field("authenticator_data");
        Assert.True(TestSigner.Verify(key.PublicKeyDer, authData.Concat(SHA256.HashData(client)).ToArray(), Field("signature")));
        return a;
    }

    /// <summary>Each header verifies for its own request with the other's hash, under one Windows Hello.</summary>
    [Fact]
    public async Task Each_header_verifies_as_KIFF_verifies_a_request_with_another()
    {
        using var key = new TestSigner();
        var auth = new FakeAuthenticator(key);
        var connect = Request("connect", "/v1/connections/batch", gateway: true, """{"tools":[{"name":"tafs_state"}]}""");
        var draft = Request("draft", "/v1/me/agent-drafts", gateway: false, """{"agent_id":"coach","label":"Coach"}""");

        var (header, batch, cards) = await RequestsTogether.SignAsync(new PasskeyApprover(auth, CredentialId, true), Tenant, connect, draft, At);

        Assert.Equal("KIFF-Passkey-Assertion", header);
        Assert.Equal(1, auth.Asked);
        var a1 = Verify(key, connect, batch);
        var a2 = Verify(key, draft, cards);
        Assert.Equal(a1, a2);
        // Each carries the other's hash.
        Assert.EndsWith($",with=sha256:{Hex(KiffRequest("POST", draft.Path, draft.Body, At))}", batch);
        Assert.EndsWith($",with=sha256:{Hex(KiffRequest("POST", connect.Path, connect.Body, At))}", cards);
    }

    /// <summary>The message, line by line, as KIFF's RequestsMessage writes it: sorted however the hashes come.</summary>
    [Fact]
    public void The_message_is_KIFF_s_kiff_requests_v1()
    {
        var lo = new string('a', 64);
        var hi = new string('b', 64);
        var expected = $"kiff.requests.v1\n{Tenant}\n{At}\n{lo}\n{hi}";
        Assert.Equal(expected, Encoding.UTF8.GetString(RequestsTogether.Message(Tenant, At, hi, lo)));
        Assert.Equal(expected, Encoding.UTF8.GetString(RequestsTogether.Message(Tenant, At, lo, hi)));
    }
}
