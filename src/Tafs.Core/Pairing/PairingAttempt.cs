using System.Security.Cryptography;
using Tafs.Core.Approvals;
using Tafs.Core.Kiff;

namespace Tafs.Core.Pairing;

/// <summary>Makes this PC's passkey: with the pairing's challenge when the claim proves it, otherwise a random one.</summary>
public delegate Task<NewPasskey> MakePasskey(string? challenge, CancellationToken cancel);

/// <summary>
/// One pairing attempt's offer: the device key, and this PC's own passkey when Windows Hello makes one.
/// A new code keeps the passkey it already has, so retrying doesn't leave passkeys behind in Windows' list.
/// When the claim proves the passkey, its challenge holds the nonce, so each attempt makes it again.
/// </summary>
public static class PairingAttempt
{
    /// <summary>
    /// The passkey's user id: sha256 of the device key, the same on every attempt with that key. With a stable
    /// user id, a passkey made again for this PC is for the same user, not one more.
    /// </summary>
    public static byte[] PasskeyUserId(byte[] devicePub) => SHA256.HashData(devicePub);

    /// <summary>
    /// The offer, and the passkey to keep for the next attempt. With no passkey (no Windows Hello, or the person
    /// closed it), the offer carries no credential: the PC then approves through the phone or the account's passkeys.
    /// </summary>
    public static async Task<(PairingOffer Offer, NewPasskey? Keep)> OfferAsync(
        byte[] devicePub, string name, MakePasskey? makePasskey, NewPasskey? kept, DateTimeOffset now,
        bool withPasskeyProof, CancellationToken cancel, byte[]? nonce = null)
    {
        nonce ??= RandomNumberGenerator.GetBytes(32);
        var credential = withPasskeyProof ? null : kept;
        if (credential is null && makePasskey is not null)
        {
            var challenge = withPasskeyProof ? PairingClaim.PasskeyChallenge(KiffSigning.Base64Url(nonce), devicePub) : null;
            try
            {
                credential = await makePasskey(challenge, cancel);
            }
            catch (Exception e) when (e is PasskeyRefusedException or InvalidDataException
                || (e is OperationCanceledException && !cancel.IsCancellationRequested))
            {
                credential = null; // closed, or refused: pair without it rather than not at all
            }
        }
        var offer = PairingOffer.Create(devicePub, approvalPub: null, name, now, nonce, credential: credential,
            withPasskeyProof: withPasskeyProof);
        return (offer, credential);
    }

    /// <summary>
    /// The passkey a new attempt leaves unused: the one kept before, when the attempt made another (with the claim's proof,
    /// each attempt makes its own). None when it was kept, or there was none.
    /// </summary>
    public static NewPasskey? Replaced(NewPasskey? kept, NewPasskey? now) =>
        kept is not null && now is not null && !kept.CredentialId.AsSpan().SequenceEqual(now.CredentialId) ? kept : null;

    /// <summary>
    /// When a code expires or the page closes, the passkey its QR offered and that no session holds: KIFF never registered
    /// it, so it's removed from Windows' list rather than left behind. One a paired session holds stays.
    /// </summary>
    public static NewPasskey? LeftOver(NewPasskey? kept, bool paired) => paired ? null : kept;

    /// <summary>
    /// Removes an unused passkey with <paramref name="delete"/> (<see cref="WindowsHello.DeletePlatformCredential"/>), quietly:
    /// a failure leaves it in Windows' list, never stops the pairing. True when it went.
    /// </summary>
    public static bool Discard(NewPasskey? unused, Func<byte[], bool> delete)
    {
        if (unused is null) return false;
        try { return delete(unused.CredentialId); }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException) { return false; }
    }
}

/// <summary>
/// The passkey the QR offers, and whether KIFF has answered for it, so an unused one is removed and a registered one never
/// is. Claimed the moment KIFF hands over a session, before it's kept: a code that expires, or a page that closes, while
/// the session is being kept then removes nothing (kiff-qa on #109).
/// </summary>
public sealed class OfferedPasskey
{
    /// <summary>The passkey the current offer carries; none without Windows Hello, or once released.</summary>
    public NewPasskey? Current { get; private set; }

    /// <summary>KIFF answered with a session for it: it's the session's, whatever expires or closes meanwhile.</summary>
    public bool Claimed { get; private set; }

    /// <summary>A new attempt's passkey: the one it replaced and left unused, to remove; none once claimed.</summary>
    public NewPasskey? Offer(NewPasskey? now)
    {
        var replaced = Claimed ? null : PairingAttempt.Replaced(Current, now);
        Current = now;
        return replaced;
    }

    /// <summary>KIFF handed over a session: from now on nothing releases the passkey.</summary>
    public void Claim() => Claimed = true;

    /// <summary>A code expired, or the page closed: the passkey no session holds, taken off the offer to remove; none once claimed.</summary>
    public NewPasskey? Release()
    {
        if (Claimed || Current is not { } unused) return null;
        Current = null;
        return unused;
    }
}
