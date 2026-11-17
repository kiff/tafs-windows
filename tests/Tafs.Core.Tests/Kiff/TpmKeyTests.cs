using System.Runtime.Versioning;
using System.Security.Cryptography;
using Tafs.Core.Kiff;
using Tafs.Core.Pairing;

namespace Tafs.Core.Tests.Kiff;

/// <summary>Runs only on Windows with a TPM: CI's Windows job, and the team's PCs. Elsewhere it's skipped, saying why.</summary>
public sealed class TpmFactAttribute : FactAttribute
{
    public TpmFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "the TPM key is Windows only";
        else if (!TpmKey.IsAvailable()) Skip = "this Windows has no TPM that makes P-256 keys";
    }
}

[Part("pairing")]
[SupportedOSPlatform("windows")]
public class TpmKeyTests : IDisposable
{
    // A name of its own, so a test never touches a real device key.
    readonly string name = "tafs test key " + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        if (OperatingSystem.IsWindows() && TpmKey.IsAvailable()) TpmKey.Delete(name);
    }

    [TpmFact]
    public async Task It_signs_in_DER_and_KIFF_s_check_verifies_it()
    {
        using var key = TpmKey.OpenOrCreate(name);
        var message = KiffSigning.RequestV1("t1", "GET", "/v1/me", [], DateTimeOffset.UtcNow);
        var signature = await key.SignDerAsync(message);

        Assert.Equal(0x30, signature[0]); // a DER SEQUENCE, never raw r‖s
        Assert.True(TestSigner.Verify(key.PublicKeyDer, message, signature));
        Assert.True(KiffSigning.IsP256Spki(key.PublicKeyDer));
    }

    /// <summary>Open never makes a key: a read's proof signs only with a key that's there.</summary>
    [TpmFact]
    public void Open_throws_for_a_key_that_isn_t_there_and_never_makes_one()
    {
        Assert.Throws<CryptographicException>(() => TpmKey.Open(name));
        Assert.False(TpmKey.Exists(name));
        using (TpmKey.OpenOrCreate(name)) { }
        using var opened = TpmKey.Open(name);
        Assert.True(KiffSigning.IsP256Spki(opened.PublicKeyDer));
    }

    [TpmFact]
    public void The_same_key_opens_again_and_its_private_half_never_leaves()
    {
        byte[] first;
        using (var key = TpmKey.OpenOrCreate(name)) first = key.PublicKeyDer;
        Assert.True(TpmKey.Exists(name));

        using var again = TpmKey.OpenOrCreate(name);
        Assert.Equal(first, again.PublicKeyDer);
        using var cng = CngKey.Open(name, TpmKey.Provider);
        Assert.Equal(CngExportPolicies.None, cng.ExportPolicy);
        Assert.Throws<CryptographicException>(() => cng.Export(CngKeyBlobFormat.EccPrivateBlob));
    }

    [TpmFact]
    public void Deleted_it_can_t_come_back()
    {
        byte[] first;
        using (var key = TpmKey.OpenOrCreate(name)) first = key.PublicKeyDer;
        TpmKey.Delete(name);
        Assert.False(TpmKey.Exists(name));
        using var fresh = TpmKey.OpenOrCreate(name);
        Assert.NotEqual(first, fresh.PublicKeyDer);
    }

    [TpmFact]
    public async Task It_makes_a_pairing_claim_KIFF_accepts()
    {
        using var key = TpmKey.OpenOrCreate(name);
        var offer = PairingOffer.Create(key.PublicKeyDer, null, "tafs on this Windows PC", DateTimeOffset.UtcNow);
        using var body = System.Text.Json.JsonDocument.Parse(await PairingClaim.BodyAsync(offer, key));
        var proof = Convert.FromBase64String(body.RootElement.GetProperty("proof").GetString()!);
        Assert.True(TestSigner.Verify(key.PublicKeyDer, PairingClaim.ProofMessage(offer.Nonce, key.PublicKeyDer), proof));
    }

    /// <summary>
    /// Each pairing makes a key of its own (the owner's test, 17 Nov): KIFF refuses a key that's already a device's, so the
    /// same name twice would never pair again once this PC's device was removed.
    /// </summary>
    [Fact]
    public void Each_pairing_has_a_key_name_of_its_own()
    {
        var a = TpmKey.NewPairingKeyName();
        var b = TpmKey.NewPairingKeyName();
        Assert.NotEqual(a, b);
        Assert.StartsWith(TpmKey.PairingKeyName + " ", a);
        Assert.NotEqual(TpmKey.DeviceKeyName, a);
    }
}
