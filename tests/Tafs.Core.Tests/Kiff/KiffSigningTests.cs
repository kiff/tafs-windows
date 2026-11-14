using System.Text;
using Tafs.Core.Kiff;

namespace Tafs.Core.Tests.Kiff;

/// <summary>Against the worked example KIFF's own verifier checks (KIFF's desktop client contract, §2.4).</summary>
[Part("pairing")]
public class KiffSigningTests
{
    // A documentation key from KIFF's page: never used for anything but this test.
    internal const string ExampleKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEuq6uAMg3330eLgKNwjg8RTxXLfC164Fzksg+3gI1gJsyV4iDPzJLgLSiMHvLSfKZg1w8QqHS5+XNYdilwVT9Yg==";

    static readonly byte[] Body = Encoding.UTF8.GetBytes(
        """{"message":"kiff.window.v1","expires":"2026-11-14T18:05:00Z","nonce":"n0nce-example-0123456789"}""");
    static readonly DateTimeOffset SignedAt = new(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);

    static byte[] ExampleMessage() =>
        KiffSigning.RequestV1("01exampletenant0000000000", "post", "/v1/me/window", Body, SignedAt);

    [Fact]
    public void The_body_hash_matches_the_example() =>
        Assert.Equal("88c1f44661cc6ee2767971ef3fba6a8d71569987695534fbf2cb26ad434a3f72", KiffSigning.Sha256Hex(Body));

    [Fact]
    public void The_request_message_matches_the_example_byte_for_byte()
    {
        Assert.Equal(
            "kiff.request.v1\n01exampletenant0000000000\nPOST\n/v1/me/window\n" +
            "88c1f44661cc6ee2767971ef3fba6a8d71569987695534fbf2cb26ad434a3f72\n2026-11-14T18:00:00Z",
            Encoding.UTF8.GetString(ExampleMessage()));
        Assert.Equal("6190890c9dae8c613e2930d15dde98e98416dfd7074083219bdb017e5e6b74be", KiffSigning.Sha256Hex(ExampleMessage()));
    }

    [Fact]
    public void The_example_signature_verifies_as_DER_over_the_message()
    {
        var sig = Convert.FromBase64String(
            "MEUCIBHu5hb8DkEcTgrMlRGZAqi4CSYe4Pb2UyeFztiUNs9YAiEAgzTPpXwDIEthTaifyK8u12QGLKNBT94E3kSf7CQkyaw=");
        Assert.True(TestSigner.Verify(Convert.FromBase64String(ExampleKey), ExampleMessage(), sig));
    }

    [Fact]
    public async Task A_signer_s_header_is_t_then_v1_in_standard_base64_DER()
    {
        using var signer = new TestSigner();
        var sig = await signer.SignDerAsync(ExampleMessage());
        Assert.Equal(0x30, sig[0]); // ASN.1 SEQUENCE: DER, not raw r‖s
        var header = KiffSigning.SignatureHeaderV1(SignedAt, sig);
        Assert.StartsWith("t=2026-11-14T18:00:00Z,v1=", header);
        Assert.True(TestSigner.Verify(signer.PublicKeyDer, ExampleMessage(), Convert.FromBase64String(header[26..])));
    }

    [Fact]
    public void Time_is_UTC_to_the_second() =>
        Assert.Equal("2026-11-14T18:00:00Z", KiffSigning.Time(new DateTimeOffset(2026, 11, 14, 19, 0, 0, 400, TimeSpan.FromHours(1))));

    [Fact]
    public void Base64url_has_no_padding_and_round_trips()
    {
        var der = Convert.FromBase64String(ExampleKey);
        var url = KiffSigning.Base64Url(der);
        Assert.DoesNotContain('=', url);
        Assert.DoesNotContain('+', url);
        Assert.DoesNotContain('/', url);
        Assert.Equal(der, KiffSigning.FromBase64Url(url));
    }

    [Fact]
    public void Only_a_P256_SPKI_is_a_key()
    {
        Assert.True(KiffSigning.IsP256Spki(Convert.FromBase64String(ExampleKey)));
        using var p384 = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP384);
        Assert.False(KiffSigning.IsP256Spki(p384.ExportSubjectPublicKeyInfo()));
        Assert.False(KiffSigning.IsP256Spki(new byte[] { 1, 2, 3 }));
    }
}
