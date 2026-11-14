using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using Tafs.Core.Words;

namespace Tafs.Core.Pairing;

/// <summary>Where the pairing screen stands.</summary>
public enum PairingStep
{
    /// <summary>The QR and the check code are up; the phone hasn't approved yet.</summary>
    Waiting,
    Expired,
    Failed,
    Added,
}

/// <summary>The lines the pairing screen shows at one step. A null line isn't shown.</summary>
public sealed record PairingLines(
    string Title,
    string? Steps,
    string? CheckLead,
    string? CheckCode,
    string Status,
    string? NewCode,
    bool ShowsQr);

/// <summary>The notice a view-only session shows above every screen (§4.3).</summary>
public sealed record ViewOnlyLines(string Title, string Lead, string Add);

/// <summary>
/// The pairing screen (desktop spec §4.2): this computer shows a QR, the person scans it with the tafs
/// phone app, checks the code and approves with the fingerprint, and the screen waits for KIFF.
/// Every line comes from the word book; nothing here touches the network.
/// </summary>
public static class PairingScreen
{
    /// <summary>KIFF's limit on a device name.</summary>
    public const int NameLimit = 64;

    /// <summary>This computer, in the person's language: "Windows PC".</summary>
    public static string Device(WordBook w) => w.Get("device.windows");

    /// <summary>The name the QR gives this computer, "tafs on this Windows PC", cut to KIFF's limit.</summary>
    public static string DeviceName(WordBook w)
    {
        var name = w.Get("pair.device_name", ("device", Device(w)));
        if (name.Length <= NameLimit) return name;
        var cut = char.IsHighSurrogate(name[NameLimit - 1]) ? NameLimit - 1 : NameLimit;
        return name[..cut];
    }

    /// <summary>Where a claim's answer leaves the screen. Pending and slow_down keep it waiting.</summary>
    public static PairingStep After(ClaimStatus status) => status switch
    {
        ClaimStatus.Pending or ClaimStatus.SlowDown => PairingStep.Waiting,
        ClaimStatus.Expired => PairingStep.Expired,
        ClaimStatus.Paired => PairingStep.Added,
        _ => PairingStep.Failed,
    };

    /// <summary>
    /// Why a claim failed, in the person's words: KIFF's status, never its body (a 200 holds the token).
    /// </summary>
    public static string Why(WordBook w, ClaimResult result)
    {
        var status = result.Status == ClaimStatus.InvalidProof
            ? "403"
            : new string((result.Detail ?? "").TakeWhile(char.IsAsciiDigit).ToArray());
        return w.Get("error.kiff_answered", ("status", status.Length > 0 ? status : "?"));
    }

    public static PairingLines Lines(WordBook w, PairingStep step, PairingOffer offer, string? why = null)
    {
        var device = ("device", Device(w));
        var title = w.Get("pair.title", device);
        var newCode = w.Get("pair.new_code");
        return step switch
        {
            // The steps name the phone's own menu, in the same language: Account, Devices, Add a computer.
            PairingStep.Waiting => new PairingLines(title,
                w.Get("pair.steps",
                    ("account", w.Get("settings.title")),
                    ("devices", w.Get("settings.devices.title")),
                    ("pair", w.Get("settings.devices.pair"))),
                w.Get("pair.check_code_lead", device),
                offer.CheckCode(),
                w.Get("pair.waiting"),
                NewCode: null,
                ShowsQr: true),
            PairingStep.Expired => new PairingLines(title, null, null, null, w.Get("pair.code_expired"), newCode, false),
            PairingStep.Failed => new PairingLines(title, null, null, null,
                w.Get("pair.failed", device, ("why", why ?? throw new ArgumentNullException(nameof(why)))), newCode, false),
            // A computer with no approval key of its own approves through the phone or a passkey.
            PairingStep.Added => new PairingLines(title, null, null, null,
                w.Get(offer.Kind == ApprovalKind.Hello ? "pair.added" : "pair.added_passkey", device), null, false),
            _ => throw new ArgumentOutOfRangeException(nameof(step)),
        };
    }

    /// <summary>
    /// A P-256 public key whose private half exists nowhere: a point found from a random x, never from a
    /// secret. The screen shows it until the device key moves to the TPM; nothing can sign a claim for it.
    /// </summary>
    public static byte[] KeyWithNoPrivateHalf()
    {
        var p = BigInteger.Parse("0ffffffff00000001000000000000000000000000ffffffffffffffffffffffff", NumberStyles.HexNumber);
        var b = BigInteger.Parse("05ac635d8aa3a93e7b3ebbd55769886bc651d06b0cc53b0f63bce3c3e27d2604b", NumberStyles.HexNumber);
        while (true)
        {
            var x = new BigInteger(RandomNumberGenerator.GetBytes(32), isUnsigned: true, isBigEndian: true) % p;
            var rhs = ((BigInteger.ModPow(x, 3, p) - 3 * x + b) % p + p) % p;
            var y = BigInteger.ModPow(rhs, (p + 1) / 4, p); // p ≡ 3 (mod 4)
            if (BigInteger.ModPow(y, 2, p) == rhs) return SpkiPrefix.Concat(Fixed32(x)).Concat(Fixed32(y)).ToArray();
            // About half of all x have no point: try another.
        }
    }

    /// <summary>SubjectPublicKeyInfo for an uncompressed P-256 point, up to the 0x04.</summary>
    static readonly byte[] SpkiPrefix = Convert.FromHexString("3059301306072a8648ce3d020106082a8648ce3d03010703420004");

    static byte[] Fixed32(BigInteger n)
    {
        var bytes = n.ToByteArray(isUnsigned: true, isBigEndian: true);
        return new byte[32 - bytes.Length].Concat(bytes).ToArray();
    }

    public static ViewOnlyLines ViewOnly(WordBook w)
    {
        var device = ("device", Device(w));
        return new ViewOnlyLines(w.Get("pair.view_only.title"), w.Get("pair.view_only.lead", device), w.Get("pair.view_only.add", device));
    }
}
