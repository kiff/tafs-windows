using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.EC;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Math.EC;
using Org.BouncyCastle.Utilities;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Record;

/// <summary>
/// This PC's check of a KIFF receipt (kiff/tafs#167), as iOS's and Android's <c>ReceiptCheck</c> and KIFF's own verification:
/// the signed digest is keccak256(ascii(sha256hex(causal_chain))), and the signer recovered from the 65-byte signature must
/// be KIFF's. The chain's bytes are taken from the answer as KIFF wrote them, never re-encoded (<see cref="RawJson"/>). Only
/// the chain is signed: what the sheet shows as checked comes from its steps.
/// </summary>
public abstract record ReceiptCheck
{
    /// <summary>The signature recovers to KIFF's signer over this chain.</summary>
    public sealed record Verified(string Signer) : ReceiptCheck;

    /// <summary>KIFF signs in its next sweep, about 30 s after the decision.</summary>
    public sealed record NotSignedYet : ReceiptCheck;

    /// <summary>It doesn't check out, and why.</summary>
    public sealed record Failed(string Why) : ReceiptCheck;

    /// <summary>
    /// KIFF's receipt signer in production (kiff/tafs#167): one key for every account, the deployer of KIFF's anchor contract
    /// and the sender of its anchor transactions. A rotation ships in a new build first.
    /// </summary>
    public const string KiffSigner = "0x2f297affd7b62ef712a1e3158421ccb4bf2efcbe";

    public const string Algorithm = "secp256k1+keccak256";

    /// <summary>
    /// The check, in iOS's order: a receipt that doesn't read, or repeats a key at its top or in its attestation, isn't one
    /// KIFF wrote; no attestation yet is not signed yet; then the algorithm, the chain, the signature's shape, and the signer
    /// recovered, which must be the one it declares and KIFF's.
    /// </summary>
    public static ReceiptCheck Check(WordBook w, byte[] response, string signer = KiffSigner)
    {
        JsonElement receipt;
        try
        {
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return new Failed(w.Get("receipt.check.unreadable"));
            receipt = doc.RootElement.Clone();
        }
        catch (JsonException) { return new Failed(w.Get("receipt.check.unreadable")); }
        // A key written twice is read as one copy here and maybe the other elsewhere: the chain hashed could differ from the
        // one another reader shows. KIFF's encoder never repeats a key. An attestation that isn't an object yet (null: not
        // signed yet) has no keys to repeat.
        var attestationKeys = RawJson.Value(response, "attestation") is { } raw ? RawJson.Keys(raw) ?? [] : [];
        if (!RawJson.KeysAreUnique(response) || attestationKeys.Distinct().Count() != attestationKeys.Count)
            return new Failed(w.Get("receipt.check.unreadable"));
        if (Lenient.Field(receipt, "attestation") is not { ValueKind: JsonValueKind.Object } attestation) return new NotSignedYet();
        if (Lenient.Text(attestation, "algorithm") != Algorithm) return new Failed(w.Get("receipt.check.unknown_signature"));
        if (RawJson.Value(response, "causal_chain") is not { } chain) return new Failed(w.Get("receipt.check.no_steps"));
        if (Lenient.Text(attestation, "signature") is not { } hex || Hex(hex) is not { Length: 65 } signature)
            return new Failed(w.Get("receipt.check.malformed_signature"));
        var declared = (Lenient.Text(attestation, "wallet_address") ?? "").ToLowerInvariant();

        var chainHash = Convert.ToHexString(SHA256.HashData(chain)).ToLowerInvariant();
        var digest = Keccak256(Encoding.ASCII.GetBytes(chainHash));
        if (RecoverAddress(digest, signature) is not { } recovered || recovered != declared)
            return new Failed(w.Get("receipt.check.signature_mismatch"));
        if (recovered != signer.ToLowerInvariant()) return new Failed(w.Get("receipt.check.not_kiff"));
        return new Verified(recovered);
    }

    /// <summary>Keccak-256 as Ethereum and KIFF use it (the original padding, not SHA3-256's).</summary>
    public static byte[] Keccak256(byte[] data)
    {
        var keccak = new KeccakDigest(256);
        keccak.BlockUpdate(data, 0, data.Length);
        var output = new byte[32];
        keccak.DoFinal(output, 0);
        return output;
    }

    static readonly Org.BouncyCastle.Asn1.X9.X9ECParameters Curve = CustomNamedCurves.GetByName("secp256k1");

    /// <summary>
    /// The Ethereum-style address that signed <paramref name="digest"/>: keccak256 of the uncompressed public key recovered
    /// from the signature, its last 20 bytes. None when the signature can't be parsed or recovers no key: r and s outside
    /// 1..n−1, a recovery id outside 0–3 (after 27 is taken off one of 27 or more), an R that isn't on the curve or isn't
    /// of order n, or a key at infinity.
    /// </summary>
    public static string? RecoverAddress(byte[] digest, byte[] signature)
    {
        if (digest.Length != 32 || signature.Length != 65) return null;
        var v = (int)signature[64];
        if (v >= 27) v -= 27;
        if (v is < 0 or > 3) return null;
        try
        {
            var n = Curve.N;
            var r = new BigInteger(1, signature, 0, 32);
            var s = new BigInteger(1, signature, 32, 32);
            if (r.SignValue <= 0 || r.CompareTo(n) >= 0 || s.SignValue <= 0 || s.CompareTo(n) >= 0) return null;
            // R's x is r, or r + n for the rarer v of 2 or 3; its y's parity is v's low bit.
            var x = r.Add(n.Multiply(BigInteger.ValueOf(v / 2)));
            if (x.CompareTo(Curve.Curve.Field.Characteristic) >= 0) return null;
            var encoded = new byte[1 + (Curve.Curve.FieldSize + 7) / 8];
            BigIntegers.AsUnsignedByteArray(x, encoded, 1, encoded.Length - 1);
            encoded[0] = (byte)((v & 1) == 1 ? 0x03 : 0x02);
            var big = Curve.Curve.DecodePoint(encoded);
            if (!big.Multiply(n).IsInfinity) return null;
            var e = new BigInteger(1, digest);
            var rInv = r.ModInverse(n);
            // Q = r⁻¹(sR − eG)
            var q = ECAlgorithms.SumOfTwoMultiplies(Curve.G, e.Negate().Mod(n).Multiply(rInv).Mod(n), big, rInv.Multiply(s).Mod(n)).Normalize();
            if (q.IsInfinity) return null;
            var key = q.GetEncoded(false);
            return "0x" + Convert.ToHexString(Keccak256(key[1..])[12..]).ToLowerInvariant();
        }
        catch (ArgumentException) { return null; }
    }

    /// <summary>A hex string, with or without 0x; none when it isn't one.</summary>
    static byte[]? Hex(string s)
    {
        if (s.StartsWith("0x", StringComparison.Ordinal)) s = s[2..];
        if (s.Length % 2 != 0) return null;
        try { return Convert.FromHexString(s); }
        catch (FormatException) { return null; }
    }
}
