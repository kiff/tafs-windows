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
}
