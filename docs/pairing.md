# Pairing this computer from a phone

Adding a computer gives it approval authority, so only a device that can already approve adds it.
This app follows KIFF's desktop client contract (§2 signatures, §5 pairing). This page covers the
portable core in `Tafs.Core`; the screen and the Windows key store come in their own changes.

## The keys

| Key | Signs | Where it lives |
| :- | :- | :- |
| Device key | setup calls, the pairing claim, taps inside an approval window | the TPM, no prompt |
| Approval key | anything that grants authority | the TPM, behind Windows Hello on every use |

`IKiffSigner` is all `Tafs.Core` sees of a key: its public half (SPKI DER) and DER signatures.
**No private key is ever in this app's memory.** The software signer in the tests exists only there.
With no TPM, the computer has no approval key: it pairs as `passkey` and approves on the phone.

## Signatures (`Tafs.Core.Kiff`)

P-256, SHA-256, ECDSA **DER** (never raw r‖s) in standard base64, over fields joined by `\n`
(`kiff.request.v1`: tenant, method, path and query, sha256(body) hex, signed_at). The tests check
this against KIFF's worked example (§2.4), down to verifying its signature.

## The flow

1. **The offer.** `PairingOffer.Create` takes the two public keys, the name ("tafs on this
   Windows PC", in the person's language) and makes the QR's `kiff.pair.v1` JSON: base64url without
   padding, a 32-byte random nonce, `exp` 4.5 minutes ahead (KIFF allows 5). `kind` is `hello`
   with an approval key and `passkey` without one. No `key_store`: a TPM key is hardware.
2. **The check code.** Both screens show `CheckCode()`, the first 6 characters of
   base32(sha256(device key ‖ approval key)). The phone asks whether the computer shows it, so a
   page showing its own QR can't get the phone to add someone else's key.
3. **The phone approves** with its approval key and the person's fingerprint (`POST /v1/me/pairings`,
   the phone's side).
4. **The claim.** `PairingClaim.PollAsync` posts to `/v1/pair/claim` with no auth, proving with the
   device key that it holds the key the QR showed (`kiff.pair.claim.v1`: nonce, sha256(key) hex). It
   polls every 3 s, adds 3 s on `slow_down`, and stops on expiry, `expired_token`, `invalid_proof`
   or the session.
5. **The session** (`PairedSession`) is handed over once. Its `ToString()` never shows the token, and
   an unreadable answer is never echoed, since a 200 holds the token.

**Not yet:** the TPM keys and Hello prompt (after the key design note is reviewed); the screen (QR,
check code, the pinned `pair.*` words); the view-only state, which waits on how KIFF tells a session
it holds no approval authority.
