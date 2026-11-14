using System.Security.Cryptography;
using System.Text.Json;
using Tafs.Core.Kiff;

namespace Tafs.Core.Pairing;

/// <summary>How this computer approves once it's paired.</summary>
public enum ApprovalKind
{
    /// <summary>A Windows Hello key in the TPM: the QR carries its public half.</summary>
    Hello,
    /// <summary>No approval key (no TPM, say): approvals go through the phone or a passkey.</summary>
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

    PairingOffer(byte[] pub, byte[]? approvalPub, string name, ApprovalKind kind, string nonce, DateTimeOffset expires)
    {
        PublicKeyDer = pub;
        ApprovalPublicKeyDer = approvalPub;
        Name = name;
        Kind = kind;
        Nonce = nonce;
        Expires = expires;
    }

    public byte[] PublicKeyDer { get; }
    public byte[]? ApprovalPublicKeyDer { get; }
    public string Name { get; }
    public ApprovalKind Kind { get; }
    public string Nonce { get; }
    public DateTimeOffset Expires { get; }

    public bool IsExpired(DateTimeOffset now) => now >= Expires;

    /// <param name="devicePub">The device key's SPKI DER (signs with no prompt).</param>
    /// <param name="approvalPub">The Hello key's SPKI DER, or null for <see cref="ApprovalKind.Passkey"/>.</param>
    /// <param name="name">"tafs on this Windows PC", in the person's language: 1 to 64 characters, no control characters.</param>
    public static PairingOffer Create(byte[] devicePub, byte[]? approvalPub, string name, DateTimeOffset now, byte[]? nonce = null)
    {
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
        return new PairingOffer(devicePub, approvalPub, name, kind, KiffSigning.Base64Url(nonce), now + Lifetime);
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
            w.WriteString("nonce", Nonce);
            w.WriteString("exp", KiffSigning.Time(Expires));
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// The 6 characters both screens show before the phone asks for the fingerprint (§5.2), so a page
    /// showing its own QR can't get the person's phone to add someone else's key.
    /// </summary>
    public string CheckCode()
    {
        var bytes = ApprovalPublicKeyDer is null ? PublicKeyDer : PublicKeyDer.Concat(ApprovalPublicKeyDer).ToArray();
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
