using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Tafs.Core.Kiff;

/// <summary>
/// A P-256 key made inside this PC's TPM, through Windows' Platform Crypto Provider. Its private half never
/// leaves the TPM: Windows can't export it, and this app never sees it. It signs with no prompt, so it's
/// the device key (KIFF's desktop client contract, §4). Keys are per Windows user.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TpmKey : IKiffSigner, IDisposable
{
    /// <summary>The TPM's key store in Windows.</summary>
    public static readonly CngProvider Provider = new("Microsoft Platform Crypto Provider");

    /// <summary>The device key's name in this Windows user's key store.</summary>
    public const string DeviceKeyName = "tafs device key";

    /// <summary>
    /// The device key a pairing makes, before its own part (<see cref="NewPairingKeyName"/>): its own, since a view-only session
    /// from the browser already holds <see cref="DeviceKeyName"/>, and KIFF refuses a key that's already a device's (the
    /// contract's §5.9).
    /// </summary>
    public const string PairingKeyName = "tafs paired device key";

    /// <summary>
    /// A new key's name for each pairing (the owner's test, 17 Nov): KIFF refuses a key that's already a device's, so a PC that
    /// pairs again, after its device was removed or alongside the one it has, needs a key no device holds. The session keeps the
    /// name, and signing out or a later pairing removes that key.
    /// </summary>
    public static string NewPairingKeyName() => PairingKeyName + " " + Guid.NewGuid().ToString("N");

    readonly CngKey key;
    readonly ECDsaCng signer;

    TpmKey(CngKey key)
    {
        this.key = key;
        signer = new ECDsaCng(key);
        PublicKeyDer = signer.ExportSubjectPublicKeyInfo();
    }

    public byte[] PublicKeyDer { get; }

    /// <summary>True when this PC has a TPM that makes P-256 keys.</summary>
    public static bool IsAvailable()
    {
        // A named key, deleted at once: .NET asks for an exportable key when it has no name, which the TPM refuses.
        try
        {
            using var probe = CngKey.Create(CngAlgorithm.ECDsaP256, "tafs probe " + Guid.NewGuid().ToString("N"), Parameters());
            probe.Delete();
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>The key with this name, made in the TPM the first time.</summary>
    /// <exception cref="CryptographicException">This PC has no TPM, or it refused.</exception>
    public static TpmKey OpenOrCreate(string name = DeviceKeyName)
    {
        var key = CngKey.Exists(name, Provider)
            ? CngKey.Open(name, Provider)
            : CngKey.Create(CngAlgorithm.ECDsaP256, name, Parameters());
        return Checked(key);
    }

    /// <summary>The key with this name, only when it's there: never made here, as for a read's proof.</summary>
    /// <exception cref="CryptographicException">There's no such key, or it isn't a P-256 key in the TPM.</exception>
    public static TpmKey Open(string name) => Checked(CngKey.Open(name, Provider));

    static TpmKey Checked(CngKey key)
    {
        // The TPM names its keys "ECDSA" whatever the curve: the public key says which curve it is.
        var tpm = new TpmKey(key);
        if (key.Provider?.Provider != Provider.Provider || !KiffSigning.IsP256Spki(tpm.PublicKeyDer))
        {
            tpm.Dispose();
            throw new CryptographicException("the key isn't a P-256 key in the TPM"); // shown on screen: no name, no path
        }
        return tpm;
    }

    public static bool Exists(string name = DeviceKeyName) => CngKey.Exists(name, Provider);

    /// <summary>Removes the key from the TPM: on sign out. It can't come back.</summary>
    public static void Delete(string name = DeviceKeyName)
    {
        if (!CngKey.Exists(name, Provider)) return;
        using var key = CngKey.Open(name, Provider);
        key.Delete();
    }

    /// <summary>SHA-256, then ECDSA in the TPM, as DER (never raw r‖s).</summary>
    public Task<byte[]> SignDerAsync(byte[] message, CancellationToken cancel = default) =>
        Task.FromResult(signer.SignHash(SHA256.HashData(message), DSASignatureFormat.Rfc3279DerSequence));

    static CngKeyCreationParameters Parameters() => new()
    {
        Provider = Provider,
        // Not exportable: the private half stays in the TPM. Signing only.
        ExportPolicy = CngExportPolicies.None,
        KeyUsage = CngKeyUsages.Signing,
        KeyCreationOptions = CngKeyCreationOptions.None,
    };

    public void Dispose()
    {
        signer.Dispose();
        key.Dispose();
    }
}
