using Tafs.Core.Approvals;
using System.Text;
using System.Text.Json;
using Tafs.Core.Kiff;
using Tafs.Core.Pairing;
using Tafs.Core.Tests.Kiff;

namespace Tafs.Core.Tests.Pairing;

[Part("pairing")]
public class PairingTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 14, 19, 0, 0, TimeSpan.Zero);
    static readonly byte[] Nonce = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    static readonly byte[] ExampleKey = Convert.FromBase64String(KiffSigningTests.ExampleKey);

    [Fact]
    public void The_QR_is_kiff_pair_v1_in_base64url_with_a_hello_key()
    {
        using var device = new TestSigner();
        using var hello = new TestSigner();
        var offer = PairingOffer.Create(device.PublicKeyDer, hello.PublicKeyDer, "tafs on this Windows PC", Now, Nonce);
        using var qr = JsonDocument.Parse(offer.ToQrJson());
        var r = qr.RootElement;

        Assert.Equal(new[] { "v", "pub", "approval_pub", "name", "platform", "kind", "nonce", "exp" },
            r.EnumerateObject().Select(p => p.Name));
        Assert.Equal("kiff.pair.v1", r.GetProperty("v").GetString());
        Assert.Equal(device.PublicKeyDer, KiffSigning.FromBase64Url(r.GetProperty("pub").GetString()!));
        Assert.Equal(hello.PublicKeyDer, KiffSigning.FromBase64Url(r.GetProperty("approval_pub").GetString()!));
        Assert.Equal("windows", r.GetProperty("platform").GetString());
        Assert.Equal("hello", r.GetProperty("kind").GetString());
        Assert.Equal(KiffSigning.Base64Url(Nonce), r.GetProperty("nonce").GetString());
        Assert.Equal(43, r.GetProperty("nonce").GetString()!.Length);
        Assert.False(r.TryGetProperty("key_store", out _)); // the TPM is hardware: no key_store
    }

    [Fact]
    public void It_expires_within_KIFF_s_five_minutes()
    {
        using var device = new TestSigner();
        var offer = PairingOffer.Create(device.PublicKeyDer, null, "tafs", Now, Nonce);
        Assert.True(offer.Expires - Now <= TimeSpan.FromMinutes(5));
        Assert.False(offer.IsExpired(Now.AddMinutes(4)));
        Assert.True(offer.IsExpired(Now.AddMinutes(5)));
    }

    [Fact]
    public void Without_a_hello_key_it_is_a_passkey_offer_with_no_approval_pub()
    {
        using var device = new TestSigner();
        var offer = PairingOffer.Create(device.PublicKeyDer, null, "tafs", Now, Nonce);
        using var qr = JsonDocument.Parse(offer.ToQrJson());
        Assert.Equal("passkey", qr.RootElement.GetProperty("kind").GetString());
        Assert.False(qr.RootElement.TryGetProperty("approval_pub", out _));
    }

    [Fact]
    public void With_this_PC_s_own_passkey_the_QR_carries_its_credential_and_no_approval_pub()
    {
        using var device = new TestSigner();
        using var passkey = new TestSigner();
        var credential = new NewPasskey([1, 2, 3, 250], passkey.PublicKeyDer);
        var offer = PairingOffer.Create(device.PublicKeyDer, null, "tafs", Now, Nonce, credential: credential);
        using var qr = JsonDocument.Parse(offer.ToQrJson());
        var r = qr.RootElement;
        Assert.Equal("passkey", r.GetProperty("kind").GetString());
        Assert.False(r.TryGetProperty("approval_pub", out _));
        Assert.False(r.TryGetProperty("key_store", out _));
        var c = r.GetProperty("credential");
        Assert.Equal(new[] { "id", "public_key" }, c.EnumerateObject().Select(p => p.Name));
        Assert.Equal("AQID-g", c.GetProperty("id").GetString());
        Assert.Equal(KiffSigning.Base64Url(passkey.PublicKeyDer), c.GetProperty("public_key").GetString());
        Assert.DoesNotContain("=", c.GetProperty("public_key").GetString());
    }

    [Fact]
    public void The_passkey_s_pairing_challenge_is_over_the_nonce_and_the_device_key()
    {
        var nonce = KiffSigning.Base64Url(Nonce);
        var expected = KiffSigning.Base64Url(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("kiff.passkey.pair.v1\n" + nonce + "\n" + KiffSigning.Base64Url(ExampleKey))));
        Assert.Equal(expected, PairingClaim.PasskeyChallenge(nonce, ExampleKey));
        Assert.NotEqual(expected, PairingClaim.PasskeyChallenge(nonce, ExampleKey[..^1].Append((byte)0).ToArray()));
    }

    [Fact]
    public async Task The_claim_carries_the_passkey_s_creation_proof_only_when_switched_on()
    {
        using var device = new TestSigner();
        using var passkey = new TestSigner();
        var made = new NewPasskey([7], passkey.PublicKeyDer) { Proof = new PasskeyCreationProof([1, 2], [3, 4]) };
        var offer = PairingOffer.Create(device.PublicKeyDer, null, "tafs", Now, Nonce, credential: made);

        using var off = JsonDocument.Parse(await PairingClaim.BodyAsync(offer, device));
        Assert.Equal(new[] { "nonce", "pub", "proof" }, off.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.True(Features.PairingPasskeyProof, "on: KIFF checks the proof, and requires it from 1 Dec 2026");

        using var on = JsonDocument.Parse(await PairingClaim.BodyAsync(offer, device, withPasskeyProof: true));
        var made2 = on.RootElement.GetProperty("credential_proof");
        Assert.Equal(new[] { "client_data_json", "attestation_object" }, made2.EnumerateObject().Select(p => p.Name));
        Assert.Equal("AQI", made2.GetProperty("client_data_json").GetString());
        Assert.Equal("AwQ", made2.GetProperty("attestation_object").GetString());

        // An offer with no credential, or a credential with no proof, sends none even when on.
        var plain = PairingOffer.Create(device.PublicKeyDer, null, "tafs", Now, Nonce, credential: made with { Proof = null });
        using var none = JsonDocument.Parse(await PairingClaim.BodyAsync(plain, device, withPasskeyProof: true));
        Assert.False(none.RootElement.TryGetProperty("credential_proof", out _));
    }

    /// <summary>
    /// The attestation object KIFF takes, built from the create's authenticator data: fmt "none", an empty attStmt, the
    /// authData as it came, in CTAP2's canonical order, with the shortest CBOR length for each size.
    /// </summary>
    [Fact]
    public void The_creation_proof_carries_no_attestation_only_the_authenticator_data()
    {
        static byte[] Head(params byte[] rest) => [0xA3, 0x63, .. "fmt"u8, 0x64, .. "none"u8, 0x67, .. "attStmt"u8, 0xA0,
            0x68, .. "authData"u8, .. rest];
        Assert.Equal(Head(0x43, 1, 2, 3), PasskeyCreationProof.NoneAttestation([1, 2, 3]));
        var real = Enumerable.Range(0, 164).Select(i => (byte)i).ToArray();
        Assert.Equal(Head([0x58, 164, .. real]), PasskeyCreationProof.NoneAttestation(real));
        var big = new byte[300];
        Assert.Equal(Head([0x59, 0x01, 0x2C, .. big]), PasskeyCreationProof.NoneAttestation(big));

        var proof = PasskeyCreationProof.WithNoAttestation([7], real);
        Assert.Equal(new byte[] { 7 }, proof.ClientDataJson);
        Assert.Equal(PasskeyCreationProof.NoneAttestation(real), proof.AttestationObject);
    }

    [Fact]
    public void A_credential_never_comes_with_an_approval_key_a_software_key_or_the_device_key()
    {
        using var device = new TestSigner();
        using var other = new TestSigner();
        var good = new NewPasskey([1], other.PublicKeyDer);
        Assert.Throws<ArgumentException>(() => PairingOffer.Create(device.PublicKeyDer, ExampleKey, "tafs", Now, credential: good));
        Assert.Throws<ArgumentException>(() => PairingOffer.Create(device.PublicKeyDer, null, "tafs", Now, deviceKeyInSoftware: true, credential: good));
        Assert.Throws<ArgumentException>(() => PairingOffer.Create(device.PublicKeyDer, null, "tafs", Now, credential: new NewPasskey([1], device.PublicKeyDer)));
        Assert.Throws<ArgumentException>(() => PairingOffer.Create(device.PublicKeyDer, null, "tafs", Now, credential: new NewPasskey([], other.PublicKeyDer)));
        Assert.Throws<ArgumentException>(() => PairingOffer.Create(device.PublicKeyDer, null, "tafs", Now, credential: new NewPasskey(new byte[1024], other.PublicKeyDer)));
        Assert.Throws<ArgumentException>(() => PairingOffer.Create(device.PublicKeyDer, null, "tafs", Now, credential: new NewPasskey([1], [1, 2])));
    }

    [Fact]
    public void The_two_keys_must_differ_and_be_P256()
    {
        using var device = new TestSigner();
        Assert.Throws<ArgumentException>(() => PairingOffer.Create(device.PublicKeyDer, device.PublicKeyDer, "tafs", Now));
        Assert.Throws<ArgumentException>(() => PairingOffer.Create(new byte[] { 1, 2 }, null, "tafs", Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("tafs\non this PC")]
    [InlineData("an exceedingly long computer name that runs on and on past sixty-four")]
    public void The_name_is_1_to_64_characters_without_control_characters(string name)
    {
        using var device = new TestSigner();
        Assert.Throws<ArgumentException>(() => PairingOffer.Create(device.PublicKeyDer, null, name, Now));
    }

    [Fact]
    public void The_check_code_is_base32_of_sha256_of_the_keys()
    {
        // Worked out apart from this code: base32(sha256(DER)), RFC 4648, the first 6 characters.
        var alone = PairingOffer.Create(ExampleKey, null, "tafs", Now, Nonce);
        Assert.Equal("3RNU6N", alone.CheckCode());

        using var hello = new TestSigner();
        var both = PairingOffer.Create(ExampleKey, hello.PublicKeyDer, "tafs", Now, Nonce);
        Assert.NotEqual(alone.CheckCode(), both.CheckCode());
        Assert.Matches("^[A-Z2-7]{6}$", both.CheckCode());
    }

    [Fact]
    public void The_check_code_matches_the_vector_every_app_and_KIFF_pin()
    {
        // The P-256 public keys of private keys 1 and 2, SPKI DER: the same pair the phones and KIFF test.
        var one = Convert.FromHexString("3059301306072a8648ce3d020106082a8648ce3d030107034200046b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c2964fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5");
        var two = Convert.FromHexString("3059301306072a8648ce3d020106082a8648ce3d030107034200047cf27b188d034f7e8a52380304b51ac3c08969e277f21b35a60b48fc4766997807775510db8ed040293d9ac69f7430dbba7dade63ce982299e04b79d227873d1");
        Assert.Equal("H6SJNU", PairingOffer.Create(one, two, "tafs", Now, Nonce).CheckCode());
        Assert.Equal("LTJFF6", PairingOffer.Create(one, null, "tafs", Now, Nonce).CheckCode());
        // §5.2: a credential's key takes approval_pub's place, so the same pair gives the same code.
        Assert.Equal("H6SJNU", PairingOffer.Create(one, null, "tafs", Now, Nonce, credential: new NewPasskey([9], two)).CheckCode());
    }

    [Fact]
    public void A_device_key_in_software_says_so_and_pairs_only_as_passkey()
    {
        using var device = new TestSigner();
        var offer = PairingOffer.Create(device.PublicKeyDer, null, "tafs", Now, Nonce, deviceKeyInSoftware: true);
        using var qr = JsonDocument.Parse(offer.ToQrJson());
        Assert.Equal("passkey", qr.RootElement.GetProperty("kind").GetString());
        Assert.Equal("software", qr.RootElement.GetProperty("key_store").GetString());

        using var hello = new TestSigner();
        Assert.Throws<ArgumentException>(() =>
            PairingOffer.Create(device.PublicKeyDer, hello.PublicKeyDer, "tafs", Now, Nonce, deviceKeyInSoftware: true));
    }

    [Fact]
    public void The_claim_proof_message_is_nonce_then_the_key_s_hash() =>
        Assert.Equal("7b29a32f4e0f67a7b21405099d7a1e47da6533233d4855b1717504341cff812c",
            KiffSigning.Sha256Hex(PairingClaim.ProofMessage("n0nce-example", ExampleKey)));

    [Fact]
    public async Task The_claim_proves_the_device_key_the_QR_showed()
    {
        using var device = new TestSigner();
        var offer = PairingOffer.Create(device.PublicKeyDer, null, "tafs", Now, Nonce);
        using var doc = JsonDocument.Parse(await PairingClaim.BodyAsync(offer, device));
        var r = doc.RootElement;
        Assert.Equal(offer.Nonce, r.GetProperty("nonce").GetString());
        Assert.True(TestSigner.Verify(device.PublicKeyDer, PairingClaim.ProofMessage(offer.Nonce, device.PublicKeyDer),
            Convert.FromBase64String(r.GetProperty("proof").GetString()!)));

        using var other = new TestSigner();
        await Assert.ThrowsAsync<ArgumentException>(() => PairingClaim.BodyAsync(offer, other));
    }

    [Theory]
    [InlineData(400, """{"error":"authorization_pending"}""", ClaimStatus.Pending)]
    [InlineData(400, """{"error":"slow_down"}""", ClaimStatus.SlowDown)]
    [InlineData(400, """{"error":"expired_token"}""", ClaimStatus.Expired)]
    [InlineData(403, """{"error":"invalid_proof"}""", ClaimStatus.InvalidProof)]
    [InlineData(500, "oops", ClaimStatus.Failed)]
    [InlineData(200, """{"access_token":"tok"}""", ClaimStatus.Failed)]
    public void Each_claim_answer_reads_as_KIFF_means_it(int status, string body, ClaimStatus expected) =>
        Assert.Equal(expected, PairingClaim.Read(status, body).Status);

    [Fact]
    public void A_paired_session_never_prints_its_token()
    {
        var r = PairingClaim.Read(200,
            """{"access_token":"secret-token","token_type":"Bearer","expires_in":7776000,"tenant":"t1","subject":"s1","device_id":"dev_1"}""");
        Assert.Equal(ClaimStatus.Paired, r.Status);
        Assert.Equal("secret-token", r.Session!.AccessToken);
        Assert.Equal(TimeSpan.FromDays(90), r.Session.ExpiresIn);
        Assert.Equal(Tafs.Core.Kiff.ApprovalMode.View, r.Session.Mode); // no approval_mode: view
        Assert.Equal(Tafs.Core.Kiff.ApprovalMode.Passkey, PairingClaim.Read(200,
            """{"access_token":"a","expires_in":60,"tenant":"t","subject":"s","device_id":"d","approval_mode":"passkey"}""").Session!.Mode);
        Assert.DoesNotContain("secret-token", r.Session.ToString());
        Assert.DoesNotContain("secret-token", r.ToString());
    }

    [Fact]
    public async Task Polling_waits_3s_slows_down_when_told_and_stops_when_paired()
    {
        using var device = new TestSigner();
        var offer = PairingOffer.Create(device.PublicKeyDer, null, "tafs", Now, Nonce);
        var answers = new Queue<(int, string)>(new[]
        {
            (400, """{"error":"authorization_pending"}"""),
            (400, """{"error":"slow_down"}"""),
            (200, """{"access_token":"x","expires_in":60,"tenant":"t","subject":"s","device_id":"d"}"""),
        });
        var delays = new List<TimeSpan>();
        var result = await PairingClaim.PollAsync(offer, device,
            (_, _) => Task.FromResult(answers.Dequeue()), () => Now,
            (d, _) => { delays.Add(d); return Task.CompletedTask; });

        Assert.Equal(ClaimStatus.Paired, result.Status);
        Assert.Equal(new[] { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6) }, delays);
    }

    [Fact]
    public async Task Polling_stops_at_the_offer_s_expiry_without_asking_again()
    {
        using var device = new TestSigner();
        var offer = PairingOffer.Create(device.PublicKeyDer, null, "tafs", Now, Nonce);
        var calls = 0;
        var result = await PairingClaim.PollAsync(offer, device,
            (_, _) => { calls++; return Task.FromResult((400, """{"error":"x"}""")); },
            () => Now.AddMinutes(5), (_, _) => Task.CompletedTask);
        Assert.Equal(ClaimStatus.Expired, result.Status);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task A_refused_proof_stops_polling()
    {
        using var device = new TestSigner();
        var offer = PairingOffer.Create(device.PublicKeyDer, null, "tafs", Now, Nonce);
        var result = await PairingClaim.PollAsync(offer, device,
            (_, _) => Task.FromResult((403, """{"error":"invalid_proof"}""")), () => Now, (_, _) => Task.CompletedTask);
        Assert.Equal(ClaimStatus.InvalidProof, result.Status);
    }
}

[Part("pairing")]
public class PairingClaimTermsTests
{
    [Fact]
    public async Task A_claim_after_the_welcome_s_tick_carries_the_terms_and_one_without_doesn_t()
    {
        using var key = new Tafs.Core.Tests.Kiff.TestSigner();
        var offer = PairingOffer.Create(key.PublicKeyDer, null, "tafs on this Windows PC", DateTimeOffset.UtcNow, deviceKeyInSoftware: true);
        using var with = System.Text.Json.JsonDocument.Parse(await PairingClaim.BodyAsync(offer, key, termsVersion: "2026-11-14"));
        Assert.Equal("2026-11-14", with.RootElement.GetProperty("terms_version").GetString());
        using var without = System.Text.Json.JsonDocument.Parse(await PairingClaim.BodyAsync(offer, key));
        Assert.False(without.RootElement.TryGetProperty("terms_version", out _));
    }

    [Fact]
    public void KIFF_s_terms_outdated_unticks_the_welcome()
    {
        var result = PairingClaim.Read(409, """{"error":"terms_outdated"}""");
        Assert.Equal(ClaimStatus.TermsOutdated, result.Status);
        var w = Tafs.Core.Words.WordBook.Load(Tafs.Core.Tests.Strings.SharedFiles.Strings, "en");
        Assert.Equal(w.Get("onboarding.terms_changed"), PairingScreen.Why(w, result));
    }
}

[Part("pairing")]
public class GateOnPhoneTests
{
    [Fact]
    public void The_gate_offers_the_phone_instead_in_the_shared_words()
    {
        var w = Tafs.Core.Words.WordBook.Load(Tafs.Core.Tests.Strings.SharedFiles.Strings, "en");
        var gate = Tafs.Core.NeedsYou.NeedsYouScreen.Gate(w);
        Assert.Equal(w.Get("approval.security_key.on_phone"), gate.OnPhone);
        Assert.Equal(PairingScreen.ViewOnly(w).Add, gate.Add);
    }
}
