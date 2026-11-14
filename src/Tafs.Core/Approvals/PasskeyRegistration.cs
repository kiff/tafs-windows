using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Tafs.Core.Approvals;

/// <summary>The computer's new passkey, as KIFF registers it: its id and its P-256 public key, SPKI DER.</summary>
public sealed record NewPasskey(byte[] CredentialId, byte[] PublicKeyDer);

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
