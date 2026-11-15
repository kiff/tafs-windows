using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Tafs.Core.Kiff;

/// <summary>
/// A P-256 key that signs for KIFF. Its private half never leaves its store (the TPM on Windows):
/// this app only ever sees the public key and the signatures.
/// </summary>
public interface IKiffSigner
{
    /// <summary>The public key, SPKI DER.</summary>
    byte[] PublicKeyDer { get; }

    /// <summary>ECDSA P-256 over sha256(message), ASN.1 DER encoded (never raw r‖s).</summary>
    Task<byte[]> SignDerAsync(byte[] message, CancellationToken cancel = default);
}

/// <summary>KIFF's signed messages and encodings, as KIFF verifies them (KIFF's desktop client contract, §2).</summary>
public static class KiffSigning
{
    /// <summary>RFC 3339 in UTC, to the second: 2026-11-14T18:00:00Z.</summary>
    public static string Time(DateTimeOffset t) =>
        t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>Fields joined by \n, no trailing newline; an empty field is an empty line.</summary>
    public static byte[] Message(params string[] fields) => Encoding.UTF8.GetBytes(string.Join('\n', fields));

    /// <summary>
    /// kiff.request.v1: what a signed request's KIFF-Device-Signature covers. Sign the exact body bytes
    /// that are sent; re-serialising after signing breaks the hash.
    /// </summary>
    public static byte[] RequestV1(string tenant, string method, string pathAndQuery, byte[] body, DateTimeOffset signedAt) =>
        Message("kiff.request.v1", tenant, method.ToUpperInvariant(), pathAndQuery, Sha256Hex(body), Time(signedAt));

    public static string SignatureHeaderV1(DateTimeOffset signedAt, byte[] derSignature) =>
        $"t={Time(signedAt)},v1={Convert.ToBase64String(derSignature)}";

    /// <summary>
    /// kiff.request.v1 at a <c>signed_at</c> kept as written: <see cref="SigningTime.Next"/>'s, with its milliseconds, so two
    /// requests in the same second never sign the same time (the iPad's <c>SigningTime</c>, used verbatim).
    /// </summary>
    public static byte[] RequestV1(string tenant, string method, string pathAndQuery, byte[] body, string signedAt) =>
        Message("kiff.request.v1", tenant, method.ToUpperInvariant(), pathAndQuery, Sha256Hex(body), signedAt);

    public static string SignatureHeaderV1(string signedAt, byte[] derSignature) => $"t={signedAt},v1={Convert.ToBase64String(derSignature)}";

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b.PadRight(b.Length + (4 - b.Length % 4) % 4, '='));
    }

    /// <summary>True when the bytes are a P-256 public key in SPKI DER.</summary>
    public static bool IsP256Spki(byte[] der)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(der, out var read);
            return read == der.Length && key.ExportParameters(false).Curve.Oid.Value == "1.2.840.10045.3.1.7";
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
