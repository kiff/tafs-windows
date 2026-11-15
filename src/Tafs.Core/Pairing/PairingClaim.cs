using System.Security.Cryptography;
using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.Kiff;

namespace Tafs.Core.Pairing;

/// <summary>The session KIFF hands over once, when the phone has approved. Its token is never printed.</summary>
public sealed record PairedSession(string AccessToken, string Tenant, string Subject, string DeviceId, TimeSpan ExpiresIn)
{
    public override string ToString() => $"PairedSession {{ Tenant = {Tenant}, DeviceId = {DeviceId}, AccessToken = (hidden) }}";
}

public enum ClaimStatus { Pending, SlowDown, Expired, InvalidProof, Paired, Failed }

public sealed record ClaimResult(ClaimStatus Status, PairedSession? Session = null, string? Detail = null);

/// <summary>
/// The computer claims its pairing (KIFF's desktop client contract, §5.4): POST /v1/pair/claim with no
/// auth, proving with the device key that it holds the key the phone approved.
/// </summary>
public static class PairingClaim
{
    public const string Path = "/v1/pair/claim";
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(3);

    /// <summary>kiff.pair.claim.v1: nonce, then sha256(pub DER) hex.</summary>
    public static byte[] ProofMessage(string nonce, byte[] pubDer) =>
        KiffSigning.Message("kiff.pair.claim.v1", nonce, KiffSigning.Sha256Hex(pubDer));

    /// <summary>
    /// The create's challenge for this PC's passkey, so the passkey is provably made for this pairing:
    /// base64url(sha256(kiff.passkey.pair.v1, nonce, pub joined by newlines)), the nonce and pub as the QR writes them.
    /// </summary>
    public static string PasskeyChallenge(string nonce, byte[] pubDer) =>
        KiffSigning.Base64Url(SHA256.HashData(KiffSigning.Message("kiff.passkey.pair.v1", nonce, KiffSigning.Base64Url(pubDer))));

    /// <param name="withPasskeyProof">Send the passkey's creation proof, when the offer has one (<see cref="Features.PairingPasskeyProof"/>).</param>
    public static async Task<byte[]> BodyAsync(PairingOffer offer, IKiffSigner deviceKey, CancellationToken cancel = default,
        bool withPasskeyProof = false)
    {
        if (!deviceKey.PublicKeyDer.AsSpan().SequenceEqual(offer.PublicKeyDer))
            throw new ArgumentException("the claim is signed by the device key the QR showed", nameof(deviceKey));
        var proof = await deviceKey.SignDerAsync(ProofMessage(offer.Nonce, offer.PublicKeyDer), cancel);
        var body = new Dictionary<string, string>
        {
            ["nonce"] = offer.Nonce,
            ["pub"] = KiffSigning.Base64Url(offer.PublicKeyDer),
            ["proof"] = Convert.ToBase64String(proof),
        };
        if (withPasskeyProof && offer.Credential?.Proof is { } made)
        {
            body["client_data_json"] = KiffSigning.Base64Url(made.ClientDataJson);
            body["attestation_object"] = KiffSigning.Base64Url(made.AttestationObject);
        }
        return JsonSerializer.SerializeToUtf8Bytes(body);
    }

    public static ClaimResult Read(int status, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (status == 200)
                return new ClaimResult(ClaimStatus.Paired, new PairedSession(
                    root.GetProperty("access_token").GetString()!,
                    root.GetProperty("tenant").GetString()!,
                    root.GetProperty("subject").GetString()!,
                    root.GetProperty("device_id").GetString()!,
                    TimeSpan.FromSeconds(root.GetProperty("expires_in").GetInt64())));

            var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            return (status, error) switch
            {
                (400, "authorization_pending") => new ClaimResult(ClaimStatus.Pending),
                (400, "slow_down") => new ClaimResult(ClaimStatus.SlowDown),
                (400, "expired_token") => new ClaimResult(ClaimStatus.Expired),
                (403, "invalid_proof") => new ClaimResult(ClaimStatus.InvalidProof),
                _ => new ClaimResult(ClaimStatus.Failed, Detail: $"{status} {error}"),
            };
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Never echo the body: a 200 holds the token.
            return new ClaimResult(ClaimStatus.Failed, Detail: $"{status}, an answer this app can't read");
        }
    }

    /// <summary>
    /// Polls until the phone approves, the offer expires, or KIFF refuses. Every 3 s or more; a slow_down
    /// adds 3 s. Transport is the caller's: post sends the body to <see cref="Path"/> and returns status and body.
    /// </summary>
    public static async Task<ClaimResult> PollAsync(
        PairingOffer offer,
        IKiffSigner deviceKey,
        Func<byte[], CancellationToken, Task<(int Status, string Body)>> post,
        Func<DateTimeOffset> now,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken cancel = default)
    {
        var interval = MinimumInterval;
        while (true)
        {
            if (offer.IsExpired(now())) return new ClaimResult(ClaimStatus.Expired);
            var (status, body) = await post(await BodyAsync(offer, deviceKey, cancel, Features.PairingPasskeyProof), cancel);
            var result = Read(status, body);
            switch (result.Status)
            {
                case ClaimStatus.Pending: break;
                case ClaimStatus.SlowDown: interval += MinimumInterval; break;
                default: return result;
            }
            await delay(interval, cancel);
        }
    }
}
