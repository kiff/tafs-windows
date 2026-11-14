using System.Security.Cryptography;
using Tafs.Core.Approvals;

namespace Tafs.Core.Tests.Approvals;

[Part("approvals")]
public class PasskeyRegistrationTests
{
    const byte UP = 0x01, UV = 0x04, BE = 0x08, BS = 0x10, AT = 0x40;
    static readonly byte[] Id = { 9, 8, 7, 6, 5 };

    /// <summary>authData with attested credential data and a COSE key, as an authenticator makes it.</summary>
    static byte[] AuthData(ECParameters p, byte flags, long alg = -7, long crv = 1, string rpId = "kiff.dev")
    {
        var cose = new List<byte> { 0xA5 }; // map of 5
        cose.AddRange(new byte[] { 0x01, 0x02 });                       // kty: EC2
        cose.AddRange(new byte[] { 0x03 }); cose.AddRange(Int(alg));   // alg
        cose.AddRange(new byte[] { 0x20 }); cose.AddRange(Int(crv));   // crv (-1)
        cose.AddRange(new byte[] { 0x21, 0x58, 32 }); cose.AddRange(p.Q.X!); // x (-2)
        cose.AddRange(new byte[] { 0x22, 0x58, 32 }); cose.AddRange(p.Q.Y!); // y (-3)

        return SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rpId))
            .Concat(new byte[] { flags, 0, 0, 0, 0 })
            .Concat(new byte[16])                          // aaguid
            .Concat(new byte[] { 0, (byte)Id.Length }).Concat(Id)
            .Concat(cose).ToArray();
    }

    static byte[] Int(long v) => v >= 0 ? new[] { (byte)v } : new[] { (byte)(0x20 | (-1 - v)) };

    [Fact]
    public void A_new_ES256_passkey_gives_its_id_and_the_same_SPKI_the_key_has()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var passkey = PasskeyRegistration.Read(AuthData(key.ExportParameters(false), UP | UV | AT));
        Assert.Equal(Id, passkey.CredentialId);
        Assert.Equal(key.ExportSubjectPublicKeyInfo(), passkey.PublicKeyDer);
    }

    [Theory]
    [InlineData(UP | UV | AT | BE)]
    [InlineData(UP | UV | AT | BS)]
    [InlineData(UP | AT)]
    public void A_passkey_that_can_leave_this_computer_or_had_no_Hello_is_refused(byte flags)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Throws<PasskeyRefusedException>(() => PasskeyRegistration.Read(AuthData(key.ExportParameters(false), flags)));
    }

    [Theory]
    [InlineData(-8, 1)]   // EdDSA
    [InlineData(-7, 2)]   // P-384
    public void Only_ES256_on_P256_is_taken(long alg, long crv)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Throws<InvalidDataException>(() => PasskeyRegistration.Read(AuthData(key.ExportParameters(false), UP | UV | AT, alg, crv)));
    }

    [Fact]
    public void Another_relying_party_s_passkey_is_refused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Throws<PasskeyRefusedException>(() =>
            PasskeyRegistration.Read(AuthData(key.ExportParameters(false), UP | UV | AT, rpId: "evil.example")));
    }

    [Fact]
    public void Cut_short_or_off_curve_data_is_refused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(false);
        var data = AuthData(p, UP | UV | AT);
        Assert.Throws<InvalidDataException>(() => PasskeyRegistration.Read(data[..60]));

        p.Q.Y![31] ^= 1; // no longer on the curve
        Assert.ThrowsAny<Exception>(() => PasskeyRegistration.Read(AuthData(p, UP | UV | AT)));
    }
}
