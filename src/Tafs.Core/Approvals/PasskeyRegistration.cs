using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Tafs.Core.Approvals;

/// <summary>The computer's new passkey, as KIFF registers it: its id and its P-256 public key, SPKI DER.</summary>
public sealed record NewPasskey(byte[] CredentialId, byte[] PublicKeyDer)
{
    /// <summary>The creation's response as WebAuthn gave it, for KIFF's creation proof (<see cref="Features.PairingPasskeyProof"/>).</summary>
    public PasskeyCreationProof? Proof { get; init; }
}

/// <summary>
/// The creation proof KIFF takes: the create's clientDataJSON, kept as it is (KIFF checks its type, origin and challenge),
/// and an attestation object with no attestation (KIFF reads the credential from its authenticator data).
/// </summary>
public sealed record PasskeyCreationProof(byte[] ClientDataJson, byte[] AttestationObject)
{
    /// <summary>
    /// The proof with the attestation object built here from the authenticator data the create returned:
    /// <c>{"fmt": "none", "attStmt": {}, "authData": …}</c>, as WebAuthn's "none" conveyance writes it. KIFF takes only
    /// <c>fmt</c> "none" with an empty <c>attStmt</c>, and refuses any other as <c>credential_proof_invalid</c>, so whatever
    /// statement the authenticator's own object carries never reaches it. The authenticator data goes as it came.
    /// </summary>
    public static PasskeyCreationProof WithNoAttestation(byte[] clientDataJson, byte[] authenticatorData) =>
        new(clientDataJson, NoneAttestation(authenticatorData));

    /// <summary>
    /// CBOR, in CTAP2's canonical order (shorter keys first): a map of three, <c>fmt</c> "none", <c>attStmt</c> an empty
    /// map, <c>authData</c> the bytes.
    /// </summary>
    public static byte[] NoneAttestation(byte[] authenticatorData)
    {
        var cbor = new List<byte> { 0xA3 };
        Text(cbor, "fmt");
        Text(cbor, "none");
        Text(cbor, "attStmt");
        cbor.Add(0xA0);
        Text(cbor, "authData");
        Head(cbor, 2, authenticatorData.Length);
        cbor.AddRange(authenticatorData);
        return [.. cbor];
    }

    static void Text(List<byte> cbor, string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        Head(cbor, 3, bytes.Length);
        cbor.AddRange(bytes);
    }

    /// <summary>A CBOR head: the major type and the length, in the fewest bytes.</summary>
    static void Head(List<byte> cbor, int major, int length)
    {
        var m = (byte)(major << 5);
        if (length < 24) cbor.Add((byte)(m | length));
        else if (length <= byte.MaxValue) cbor.AddRange([(byte)(m | 24), (byte)length]);
        else if (length <= ushort.MaxValue) cbor.AddRange([(byte)(m | 25), (byte)(length >> 8), (byte)length]);
        else cbor.AddRange([(byte)(m | 26), (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length]);
    }
}

/// <summary>
/// Reads the authenticator data of a newly made passkey (attested credential data, then the COSE key)
/// and gives KIFF what it registers. Refuses what could never approve for this computer.
/// </summary>
public static class PasskeyRegistration
{
    public static NewPasskey Read(byte[] authenticatorData)
    {
        var head = AuthenticatorData.Parse(authenticatorData);
        var refusal = PasskeyApprover.Refusal(head);
        if (refusal is not null) throw new PasskeyRefusedException(refusal);
        if (!head.HasCredentialData) throw new InvalidDataException("no credential in the authenticator data");

        // 37: aaguid (16), then the credential id's length (2, big-endian), the id, then the COSE key.
        var span = authenticatorData.AsSpan(37);
        if (span.Length < 18) throw new InvalidDataException("the credential data is cut short");
        int idLength = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(16, 2));
        if (idLength == 0 || span.Length < 18 + idLength) throw new InvalidDataException("the credential id is cut short");
        var id = span.Slice(18, idLength).ToArray();

        var pos = 18 + idLength;
        var cose = ReadCoseKey(span[pos..]);
        return new NewPasskey(id, ToSpki(cose));
    }

    /// <summary>
    /// An ES256 COSE_Key: kty 2 (EC2), alg -7, crv 1 (P-256), x and y of 32 bytes. Anything else is refused:
    /// KIFF accepts only ES256.
    /// </summary>
    static (byte[] X, byte[] Y) ReadCoseKey(ReadOnlySpan<byte> cbor)
    {
        var reader = new Cbor(cbor);
        var entries = reader.MapHeader();
        long? kty = null, alg = null, crv = null;
        byte[]? x = null, y = null;
        for (var i = 0; i < entries; i++)
        {
            var key = reader.Integer();
            switch (key)
            {
                case 1: kty = reader.Integer(); break;
                case 3: alg = reader.Integer(); break;
                case -1: crv = reader.Integer(); break;
                case -2: x = reader.Bytes(); break;
                case -3: y = reader.Bytes(); break;
                default: throw new InvalidDataException($"unexpected COSE key label {key}");
            }
        }
        if (kty != 2 || alg != -7 || crv != 1 || x is not { Length: 32 } || y is not { Length: 32 })
            throw new InvalidDataException("the passkey isn't ES256 on P-256");
        return (x, y);
    }

    static byte[] ToSpki((byte[] X, byte[] Y) point)
    {
        using var key = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = point.X, Y = point.Y },
        }); // throws if the point isn't on the curve
        return key.ExportSubjectPublicKeyInfo();
    }

    /// <summary>Just enough CBOR for a COSE key: a map, small integers and byte strings.</summary>
    ref struct Cbor(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> rest = data;

        public int MapHeader() => (int)Head(5);
        public byte[] Bytes() { var n = (int)Head(2); var b = Take(n); return b.ToArray(); }

        public long Integer()
        {
            var major = rest.IsEmpty ? -1 : rest[0] >> 5;
            if (major == 0) return (long)Head(0);
            if (major == 1) return -1 - (long)Head(1);
            throw new InvalidDataException("expected an integer in the COSE key");
        }

        ulong Head(int expectedMajor)
        {
            if (rest.IsEmpty || rest[0] >> 5 != expectedMajor) throw new InvalidDataException("unexpected CBOR in the COSE key");
            var info = rest[0] & 31;
            rest = rest[1..];
            return info switch
            {
                < 24 => (ulong)info,
                24 => Take(1)[0],
                25 => BinaryPrimitives.ReadUInt16BigEndian(Take(2)),
                26 => BinaryPrimitives.ReadUInt32BigEndian(Take(4)),
                _ => throw new InvalidDataException("CBOR length too long for a COSE key"),
            };
        }

        ReadOnlySpan<byte> Take(int n)
        {
            if (rest.Length < n) throw new InvalidDataException("the COSE key is cut short");
            var taken = rest[..n];
            rest = rest[n..];
            return taken;
        }
    }
}
