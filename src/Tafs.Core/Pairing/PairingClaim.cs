using System.Security.Cryptography;
using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.Kiff;

namespace Tafs.Core.Pairing;

/// <summary>The session KIFF hands over once, when the phone has approved. Its token is never printed.</summary>
/// <param name="Mode">From approval_mode: unknown or missing is View.</param>
public sealed record PairedSession(string AccessToken, string Tenant, string Subject, string DeviceId, TimeSpan ExpiresIn,
    ApprovalMode Mode = ApprovalMode.View)
{
    public override string ToString() => $"PairedSession {{ Tenant = {Tenant}, DeviceId = {DeviceId}, AccessToken = (hidden) }}";
}

/// <summary>
/// Where a claim stands. <see cref="Refused"/>: the phone's approve was refused, and KIFF says so to this PC only, the one
/// that proves the refused key (the claim's <c>403 pairing_refused</c>, KIFF's desktop client contract, §5.4).
/// </summary>
public enum ClaimStatus { Pending, SlowDown, Expired, InvalidProof, Paired, Failed, Refused, TermsOutdated }

/// <param name="Reason">For <see cref="ClaimStatus.Refused"/>: KIFF's reason code (<c>key_registered</c>), never its message.</param>
public sealed record ClaimResult(ClaimStatus Status, PairedSession? Session = null, string? Detail = null, string? Reason = null);

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

    /// <summary>The challenge the passkey's creation signed, from its clientDataJSON; null with no proof or no challenge.</summary>
    public static string? ProofChallenge(NewPasskey passkey)
    {
        if (passkey.Proof is not { } proof) return null;
        try
        {
            using var doc = JsonDocument.Parse(proof.ClientDataJson);
            return doc.RootElement.TryGetProperty("challenge", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    /// <param name="withPasskeyProof">Send the passkey's creation proof, when the offer has one (<see cref="Features.PairingPasskeyProof"/>).</param>
    /// <param name="termsVersion">The version the welcome's tick agreed to (<c>Onboarding</c>), which KIFF records for this PC with the
    /// session, as the iPad's <c>claimPairing(termsVersion:)</c>; KIFF answers <c>terms_outdated</c> before claiming anything when it's old.</param>
    public static async Task<byte[]> BodyAsync(PairingOffer offer, IKiffSigner deviceKey, CancellationToken cancel = default,
        bool withPasskeyProof = false, string? termsVersion = null)
    {
        if (!deviceKey.PublicKeyDer.AsSpan().SequenceEqual(offer.PublicKeyDer))
            throw new ArgumentException("the claim is signed by the device key the QR showed", nameof(deviceKey));
        var proof = await deviceKey.SignDerAsync(ProofMessage(offer.Nonce, offer.PublicKeyDer), cancel);
        var body = new Dictionary<string, object>
        {
            ["nonce"] = offer.Nonce,
            ["pub"] = KiffSigning.Base64Url(offer.PublicKeyDer),
            ["proof"] = Convert.ToBase64String(proof),
        };
        if (withPasskeyProof && offer.Credential?.Proof is { } made)
            body["credential_proof"] = new Dictionary<string, string>
            {
                ["client_data_json"] = KiffSigning.Base64Url(made.ClientDataJson),
                ["attestation_object"] = KiffSigning.Base64Url(made.AttestationObject),
            };
        if (termsVersion is { Length: > 0 }) body["terms_version"] = termsVersion;
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
                    TimeSpan.FromSeconds(root.GetProperty("expires_in").GetInt64()),
                    KeptSession.ModeOf(root.TryGetProperty("approval_mode", out var m) ? m.GetString() : null)));

            var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            return (status, error) switch
            {
                (400, "authorization_pending") => new ClaimResult(ClaimStatus.Pending),
                (400, "slow_down") => new ClaimResult(ClaimStatus.SlowDown),
                (400, "expired_token") => new ClaimResult(ClaimStatus.Expired),
                (403, "invalid_proof") => new ClaimResult(ClaimStatus.InvalidProof),
                (409, "terms_outdated") => new ClaimResult(ClaimStatus.TermsOutdated),
                (403, "pairing_refused") => new ClaimResult(ClaimStatus.Refused,
                    Reason: root.TryGetProperty("reason", out var why) && why.ValueKind == JsonValueKind.String ? why.GetString() : null),
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
        CancellationToken cancel = default,
        string? termsVersion = null)
    {
        var interval = MinimumInterval;
        while (true)
        {
            if (offer.IsExpired(now())) return new ClaimResult(ClaimStatus.Expired);
            var (status, body) = await post(await BodyAsync(offer, deviceKey, cancel, Features.PairingPasskeyProof, termsVersion), cancel);
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
