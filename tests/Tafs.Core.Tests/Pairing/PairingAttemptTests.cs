using System.Text;
using Tafs.Core.Approvals;
using Tafs.Core.Kiff;
using Tafs.Core.Pairing;
using Tafs.Core.Tests.Kiff;

namespace Tafs.Core.Tests.Pairing;

[Part("pairing")]
public class PairingAttemptTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 15, 3, 0, 0, TimeSpan.Zero);
    static readonly byte[] Nonce = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    /// <summary>A stand-in for Windows Hello: counts its prompts, and writes the challenge it was given.</summary>
    sealed class FakeHello : IDisposable
    {
        public readonly TestSigner Key = new();
        public readonly List<string?> Challenges = [];
        public Exception? Fails;

        public Task<NewPasskey> Make(string? challenge, CancellationToken cancel)
        {
            Challenges.Add(challenge);
            if (Fails is not null) throw Fails;
            var clientData = Encoding.UTF8.GetBytes($"{{\"type\":\"webauthn.create\",\"challenge\":\"{challenge ?? "random"}\"}}");
            return Task.FromResult(new NewPasskey([9, (byte)Challenges.Count], Key.PublicKeyDer) { Proof = new PasskeyCreationProof(clientData, [0xA0]) });
        }

        public void Dispose() => Key.Dispose();
    }

    [Fact]
    public async Task The_QR_carries_this_PC_s_passkey_and_a_new_code_keeps_it()
    {
        using var device = new TestSigner();
        using var hello = new FakeHello();
        var (first, keep) = await PairingAttempt.OfferAsync(device.PublicKeyDer, "tafs", hello.Make, null, Now, false, default);
        var (second, _) = await PairingAttempt.OfferAsync(device.PublicKeyDer, "tafs", hello.Make, keep, Now, false, default);

        Assert.Single(hello.Challenges); // one Windows Hello prompt, one passkey in Windows' list
        Assert.Null(hello.Challenges[0]);
        Assert.Equal(first.Credential, second.Credential);
        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.Contains("\"credential\"", second.ToQrJson());
    }

    [Fact]
    public async Task With_the_proof_each_attempt_makes_it_for_its_own_nonce()
    {
        using var device = new TestSigner();
        using var hello = new FakeHello();
        var (first, keep) = await PairingAttempt.OfferAsync(device.PublicKeyDer, "tafs", hello.Make, null, Now, true, default, Nonce);
        var (second, _) = await PairingAttempt.OfferAsync(device.PublicKeyDer, "tafs", hello.Make, keep, Now, true, default);

        Assert.Equal(2, hello.Challenges.Count);
        Assert.Equal(PairingClaim.PasskeyChallenge(KiffSigning.Base64Url(Nonce), device.PublicKeyDer), hello.Challenges[0]);
        Assert.Equal(PairingClaim.PasskeyChallenge(second.Nonce, device.PublicKeyDer), hello.Challenges[1]);
        Assert.Equal(hello.Challenges[0], PairingClaim.ProofChallenge(first.Credential!));
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("refused")]
    public async Task Without_Windows_Hello_it_pairs_with_no_credential(string how)
    {
        using var device = new TestSigner();
        using var hello = new FakeHello
        {
            Fails = how == "closed" ? new OperationCanceledException() : new PasskeyRefusedException("NotAllowedError"),
        };
        var (offer, keep) = await PairingAttempt.OfferAsync(device.PublicKeyDer, "tafs", hello.Make, null, Now, false, default);
        Assert.Null(offer.Credential);
        Assert.Null(keep);
        Assert.Equal(ApprovalKind.Passkey, offer.Kind);

        var (none, _) = await PairingAttempt.OfferAsync(device.PublicKeyDer, "tafs", makePasskey: null, null, Now, false, default);
        Assert.Null(none.Credential);
    }

    [Fact]
    public async Task Stopping_the_attempt_stops_it_rather_than_pairing_without()
    {
        using var device = new TestSigner();
        using var hello = new FakeHello { Fails = new OperationCanceledException() };
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PairingAttempt.OfferAsync(device.PublicKeyDer, "tafs", hello.Make, null, Now, false, stop.Token));
    }

    [Fact]
    public async Task With_the_proof_a_passkey_made_for_another_pairing_is_refused_before_the_QR()
    {
        using var device = new TestSigner();
        using var hello = new FakeHello();
        var made = await hello.Make(PairingClaim.PasskeyChallenge("another nonce", device.PublicKeyDer), default);

        Assert.Throws<ArgumentException>(() => PairingOffer.Create(device.PublicKeyDer, null, "tafs", Now, Nonce, credential: made, withPasskeyProof: true));
        Assert.Throws<ArgumentException>(() => PairingOffer.Create(device.PublicKeyDer, null, "tafs", Now, Nonce,
            credential: made with { Proof = null }, withPasskeyProof: true));
        // Without the proof, the challenge isn't KIFF's to check.
        Assert.NotNull(PairingOffer.Create(device.PublicKeyDer, null, "tafs", Now, Nonce, credential: made));
    }

    [Fact]
    public void The_passkey_s_user_is_the_same_for_the_same_device_key()
    {
        using var device = new TestSigner();
        using var other = new TestSigner();
        Assert.Equal(PairingAttempt.PasskeyUserId(device.PublicKeyDer), PairingAttempt.PasskeyUserId(device.PublicKeyDer));
        Assert.NotEqual(PairingAttempt.PasskeyUserId(device.PublicKeyDer), PairingAttempt.PasskeyUserId(other.PublicKeyDer));
        Assert.Equal(32, PairingAttempt.PasskeyUserId(device.PublicKeyDer).Length);
    }
}
