using System.Security.Cryptography;
using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.Kiff;

namespace Tafs.Core.Pairing;

/// <summary>How this computer approves once it's paired.</summary>
public enum ApprovalKind
{
    /// <summary>A Windows Hello key in the TPM: the QR carries its public half.</summary>
    Hello,
    /// <summary>
    /// No approval key: approvals are passkey assertions. With <see cref="PairingOffer.Credential"/>, this PC's own
    /// Windows Hello passkey, which the phone's pairing approval registers for it (§2.6, §5.1).
    /// </summary>
    Passkey,
}

/// <summary>
/// What the computer shows as a QR code so a phone that can already approve adds it
/// (KIFF's desktop client contract, §5.1). The computer keeps the nonce and its private keys, nothing else.
/// </summary>
public sealed class PairingOffer
{
    /// <summary>KIFF accepts up to 5 minutes; a little less leaves room for the clocks to differ.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(4.5);

    PairingOffer(byte[] pub, byte[]? approvalPub, string name, ApprovalKind kind, bool keyInSoftware, string nonce, DateTimeOffset expires,
        NewPasskey? credential)
    {
        Credential = credential;
        PublicKeyDer = pub;
        ApprovalPublicKeyDer = approvalPub;
        Name = name;
        Kind = kind;
        DeviceKeyInSoftware = keyInSoftware;
        Nonce = nonce;
        Expires = expires;
    }

    public byte[] PublicKeyDer { get; }
    public byte[]? ApprovalPublicKeyDer { get; }
    public string Name { get; }
    public ApprovalKind Kind { get; }

    /// <summary>This PC's own Windows Hello passkey, registered by the pairing: only with <see cref="ApprovalKind.Passkey"/>.</summary>
    public NewPasskey? Credential { get; }

    /// <summary>
    /// The device key isn't in the TPM (a PC without one). The QR then says key_store "software", and
    /// KIFF never lets that key approve: every approval goes through the phone or a passkey.
    /// </summary>
    public bool DeviceKeyInSoftware { get; }
    public string Nonce { get; }
    public DateTimeOffset Expires { get; }

    public bool IsExpired(DateTimeOffset now) => now >= Expires;

    /// <param name="devicePub">The device key's SPKI DER (signs with no prompt).</param>
    /// <param name="approvalPub">The Hello key's SPKI DER, or null for <see cref="ApprovalKind.Passkey"/>.</param>
    /// <param name="name">"tafs on this Windows PC", in the person's language: 1 to 64 characters, no control characters.</param>
    /// <param name="deviceKeyInSoftware">True when the device key isn't in the TPM: only with no approval key.</param>
    /// <param name="credential">This PC's own passkey (Windows Hello), for a passkey offer from a device key in the TPM.</param>
    public static PairingOffer Create(
        byte[] devicePub, byte[]? approvalPub, string name, DateTimeOffset now, byte[]? nonce = null, bool deviceKeyInSoftware = false,
        NewPasskey? credential = null)
    {
        if (credential is not null)
        {
            // §5.1: only with kind passkey, never with approval_pub or a software device key (that's a view device).
            if (approvalPub is not null) throw new ArgumentException("a passkey offer carries no approval key", nameof(credential));
            if (deviceKeyInSoftware) throw new ArgumentException("a software device key pairs as view, with no credential", nameof(credential));
            if (credential.CredentialId.Length is < 1 or > 1023) throw new ArgumentException("the credential id is 1 to 1023 bytes", nameof(credential));
            if (!KiffSigning.IsP256Spki(credential.PublicKeyDer)) throw new ArgumentException("the passkey must be a P-256 SPKI", nameof(credential));
            if (credential.PublicKeyDer.AsSpan().SequenceEqual(devicePub)) throw new ArgumentException("the passkey must differ from the device key", nameof(credential));
        }
        if (deviceKeyInSoftware && approvalPub is not null)
            throw new ArgumentException("a software device key pairs as passkey, with no approval key", nameof(approvalPub));
        if (!KiffSigning.IsP256Spki(devicePub))
            throw new ArgumentException("the device key must be a P-256 SPKI", nameof(devicePub));
        if (approvalPub is not null && !KiffSigning.IsP256Spki(approvalPub))
            throw new ArgumentException("the approval key must be a P-256 SPKI", nameof(approvalPub));
        if (approvalPub is not null && approvalPub.AsSpan().SequenceEqual(devicePub))
            throw new ArgumentException("the approval key must differ from the device key", nameof(approvalPub));
        if (name.Length is < 1 or > 64 || name.Any(char.IsControl))
            throw new ArgumentException("the name must be 1 to 64 characters, with no control characters", nameof(name));

        nonce ??= RandomNumberGenerator.GetBytes(32);
        if (nonce.Length != 32) throw new ArgumentException("the nonce is 32 random bytes", nameof(nonce));

        var kind = approvalPub is null ? ApprovalKind.Passkey : ApprovalKind.Hello;
        return new PairingOffer(devicePub, approvalPub, name, kind, deviceKeyInSoftware, KiffSigning.Base64Url(nonce), now + Lifetime, credential);
    }

    /// <summary>The QR's text: kiff.pair.v1, keys in base64url without padding.</summary>
    public string ToQrJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("v", "kiff.pair.v1");
            w.WriteString("pub", KiffSigning.Base64Url(PublicKeyDer));
            if (ApprovalPublicKeyDer is not null) w.WriteString("approval_pub", KiffSigning.Base64Url(ApprovalPublicKeyDer));
            w.WriteString("name", Name);
            w.WriteString("platform", "windows");
            w.WriteString("kind", Kind == ApprovalKind.Hello ? "hello" : "passkey");
            if (DeviceKeyInSoftware) w.WriteString("key_store", "software");
            if (Credential is not null)
            {
                w.WriteStartObject("credential");
                w.WriteString("id", KiffSigning.Base64Url(Credential.CredentialId));
                w.WriteString("public_key", KiffSigning.Base64Url(Credential.PublicKeyDer));
                w.WriteEndObject();
            }
            w.WriteString("nonce", Nonce);
            w.WriteString("exp", KiffSigning.Time(Expires));
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// The 6 characters both screens show before the phone asks for the fingerprint (§5.2), so a page
    /// showing its own QR can't get the person's phone to add someone else's key. With a credential, its key takes
    /// the approval key's place, so the code covers the passkey the phone registers.
    /// </summary>
    public string CheckCode()
    {
        var second = ApprovalPublicKeyDer ?? Credential?.PublicKeyDer;
        var bytes = second is null ? PublicKeyDer : PublicKeyDer.Concat(second).ToArray();
        return Base32(SHA256.HashData(bytes))[..6];
    }

    static string Base32(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var sb = new System.Text.StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }
}
