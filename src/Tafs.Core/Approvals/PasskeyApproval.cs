using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Tafs.Core.Kiff;

namespace Tafs.Core.Approvals;

/// <summary>
/// Windows Hello as a platform passkey for kiff.dev: an ES256 key in the TPM that Windows itself asks
/// the person for on every assertion. On since KIFF accepts it: a Windows start says "approval": "passkey",
/// is refused with neither key nor flag, and a passkey session approves only with KIFF-Passkey-Assertion.
/// </summary>
public static class Features
{
    public static readonly bool WindowsPasskeyApprovals = true;

    /// <summary>
    /// The pairing claim proves this PC's passkey was made for this pairing: the create's challenge is
    /// <see cref="Pairing.PairingClaim.PasskeyChallenge"/>, and the claim sends client_data_json and attestation_object.
    /// Off until KIFF takes them: the QR's credential ships as it is.
    /// </summary>
    public static readonly bool PairingPasskeyProof = false;
}

/// <summary>authenticatorData's fixed head: rpIdHash (32), flags (1), signCount (4).</summary>
public readonly record struct AuthenticatorData(byte[] RpIdHash, byte Flags, uint SignCount)
{
    public bool UserPresent => (Flags & 0x01) != 0;
    public bool UserVerified => (Flags & 0x04) != 0;
    public bool BackupEligible => (Flags & 0x08) != 0;
    public bool BackedUp => (Flags & 0x10) != 0;
    public bool HasCredentialData => (Flags & 0x40) != 0;

    public static AuthenticatorData Parse(byte[] data)
    {
        if (data.Length < 37) throw new InvalidDataException("authenticator data is shorter than 37 bytes");
        return new AuthenticatorData(data[..32], data[32], BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(33, 4)));
    }
}

/// <summary>What the authenticator returns for one assertion.</summary>
public sealed record PasskeyAssertion(byte[] AuthenticatorData, byte[] SignatureDer);

/// <summary>The Windows WebAuthn call (it shows Windows Hello). Implemented in the app; faked in tests.</summary>
public interface IPasskeyAuthenticator
{
    Task<PasskeyAssertion> GetAssertionAsync(string rpId, byte[] credentialId, byte[] clientDataJson, CancellationToken cancel);
}

/// <summary>Refused on this computer, before anything is sent: the reason is safe to show and log.</summary>
public sealed class PasskeyRefusedException(string reason) : Exception(reason);

/// <summary>
/// Approves one exact request with this computer's passkey (KIFF-Passkey-Assertion). The challenge is
/// base64url(sha256(kiff.request.v1)), so the assertion is bound to that request, as an approval key's
/// signature would be. KIFF verifies; this side refuses early what KIFF would refuse anyway.
/// </summary>
public sealed class PasskeyApprover(IPasskeyAuthenticator authenticator, byte[] credentialId, bool enabled)
{
    public const string RpId = "kiff.dev";
    public const string Origin = "https://kiff.dev";
    public const string HeaderName = "KIFF-Passkey-Assertion";

    public static string Challenge(byte[] requestMessage) => KiffSigning.Base64Url(SHA256.HashData(requestMessage));

    /// <summary>The exact clientDataJSON bytes this app builds and the authenticator hashes.</summary>
    public static byte[] ClientDataJson(byte[] requestMessage) => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
    {
        ["type"] = "webauthn.get",
        ["challenge"] = Challenge(requestMessage),
        ["origin"] = Origin,
        ["crossOrigin"] = false,
    });

    /// <summary>
    /// Why KIFF would refuse an assertion with this authenticator data, or null: the wrong relying
    /// party, no presence or no verification (Windows Hello not shown), or a passkey that can leave
    /// this computer (backup eligible or backed up), which is then not this computer's own.
    /// </summary>
    public static string? Refusal(AuthenticatorData a)
    {
        if (!a.RpIdHash.AsSpan().SequenceEqual(SHA256.HashData("kiff.dev"u8))) return "the passkey isn't for kiff.dev";
        if (!a.UserPresent || !a.UserVerified) return "Windows Hello didn't verify the person";
        if (a.BackupEligible || a.BackedUp) return "this passkey can leave this computer, so it can't approve for it";
        return null;
    }

    /// <summary>
    /// Asks Windows Hello to confirm a step that isn't a request, as the iPad's Face ID prompt over a message: one assertion
    /// with this PC's passkey, its challenge sha256(<paramref name="message"/>), checked as an approval's is. Closing
    /// Windows Hello throws <see cref="OperationCanceledException"/>; anything KIFF would refuse, <see cref="PasskeyRefusedException"/>.
    /// </summary>
    public async Task ConfirmAsync(byte[] message, CancellationToken cancel = default)
    {
        // Off, nothing asks for Windows Hello: as ApproveAsync.
        if (!enabled) throw new PasskeyRefusedException("passkey approvals are off on Windows until KIFF accepts this computer's own passkey");
        var assertion = await authenticator.GetAssertionAsync(RpId, credentialId, ClientDataJson(message), cancel);
        if (Refusal(AuthenticatorData.Parse(assertion.AuthenticatorData)) is { } refusal) throw new PasskeyRefusedException(refusal);
    }

    /// <summary>
    /// Signs the request with a fresh assertion at a <c>signed_at</c> kept as written (<see cref="Kiff.SigningTime.Next"/>'s,
    /// with its milliseconds), the same in the message and the header's <c>t=</c>, and returns the header's value.
    /// </summary>
    public async Task<string> ApproveAsync(
        string tenant, string method, string pathAndQuery, byte[] body, string signedAt, CancellationToken cancel = default)
    {
        if (!enabled) throw new PasskeyRefusedException("passkey approvals are off on Windows until KIFF accepts this computer's own passkey");

        var clientData = ClientDataJson(KiffSigning.RequestV1(tenant, method, pathAndQuery, body, signedAt));
        var assertion = await authenticator.GetAssertionAsync(RpId, credentialId, clientData, cancel);
        var refusal = Refusal(AuthenticatorData.Parse(assertion.AuthenticatorData));
        if (refusal is not null) throw new PasskeyRefusedException(refusal);

        var json = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string>
        {
            ["credential_id"] = KiffSigning.Base64Url(credentialId),
            ["client_data_json"] = KiffSigning.Base64Url(clientData),
            ["authenticator_data"] = KiffSigning.Base64Url(assertion.AuthenticatorData),
            ["signature"] = KiffSigning.Base64Url(assertion.SignatureDer),
        });
        return $"t={signedAt},a={KiffSigning.Base64Url(json)}";
    }

    /// <summary>Signs the request with a fresh assertion and returns the header's value.</summary>
    public Task<string> ApproveAsync(
        string tenant, string method, string pathAndQuery, byte[] body, DateTimeOffset signedAt, CancellationToken cancel = default) =>
        AssertAsync(KiffSigning.RequestV1(tenant, method, pathAndQuery, body, KiffSigning.Time(signedAt)), KiffSigning.Time(signedAt), cancel);

    /// <summary>
    /// One fresh assertion over <paramref name="message"/> (its challenge base64url(sha256(message))), as the header's
    /// <c>t=…,a=…</c>: a request's <c>kiff.request.v1</c>, or two requests' <c>kiff.requests.v1</c>, whose headers each add
    /// <c>with=sha256:…</c>.
    /// </summary>
    internal async Task<string> AssertAsync(byte[] message, string signedAt, CancellationToken cancel = default)
    {
        if (!enabled) throw new PasskeyRefusedException("passkey approvals are off on Windows until KIFF accepts this computer's own passkey");

        var clientData = ClientDataJson(message);
        var assertion = await authenticator.GetAssertionAsync(RpId, credentialId, clientData, cancel);
        var refusal = Refusal(AuthenticatorData.Parse(assertion.AuthenticatorData));
        if (refusal is not null) throw new PasskeyRefusedException(refusal);

        var json = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string>
        {
            ["credential_id"] = KiffSigning.Base64Url(credentialId),
            ["client_data_json"] = KiffSigning.Base64Url(clientData),
            ["authenticator_data"] = KiffSigning.Base64Url(assertion.AuthenticatorData),
            ["signature"] = KiffSigning.Base64Url(assertion.SignatureDer),
        });
        return $"t={signedAt},a={KiffSigning.Base64Url(json)}";
    }
}
