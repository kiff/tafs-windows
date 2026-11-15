# Pairing this computer from a phone

Adding a computer gives it approval authority, so only a device that can already approve adds it.
This app follows KIFF's desktop client contract (§2 signatures, §5 pairing). This page covers the
portable core in `Tafs.Core` and the screen.

## The keys

| Key | Signs | Where it lives |
| :- | :- | :- |
| Device key | setup calls, the pairing claim, taps inside an approval window | the TPM, no prompt |
| This PC's passkey | anything that grants authority, one assertion each time | the TPM, Windows Hello's own (ES256), Windows asking on every use ([approvals](approvals.md)) |

`IKiffSigner` is all `Tafs.Core` sees of a key: its public half (SPKI DER) and DER signatures.
**No private key is ever in this app's memory.** The software signer in the tests exists only there.
Windows Hello can't hold a P-256 approval key, so a Windows PC never sends `approval_pub`: it pairs as
`passkey`, with its own passkey as `credential` when it has one. With no TPM, it pairs as a view device
(`key_store: "software"`, no credential) and every approval goes to the phone.

## Signatures (`Tafs.Core.Kiff`)

P-256, SHA-256, ECDSA **DER** (never raw r‖s) in standard base64, over fields joined by `\n`
(`kiff.request.v1`: tenant, method, path and query, sha256(body) hex, signed_at). The tests check
this against KIFF's worked example (§2.4), down to verifying its signature.

## The flow

1. **The offer.** `PairingOffer.Create` takes the two public keys, the name ("tafs on this
   Windows PC", in the person's language) and makes the QR's `kiff.pair.v1` JSON: base64url without
   padding, a 32-byte random nonce, `exp` 4.5 minutes ahead (KIFF allows 5). `kind` is `hello`
   with an approval key and `passkey` without one. A device key in the TPM sends no `key_store`; a
   PC with no TPM sends `"key_store": "software"` (passkey only), and KIFF never lets that key approve.
   - **This PC's own passkey** goes in as `"credential": {"id", "public_key"}`, base64url, the key
     as SPKI DER (§5.1). Only with `kind: "passkey"`: never with `approval_pub` or a software device
     key, never equal to the device key, its id 1 to 1023 bytes. The phone's one pairing approval
     registers it for this PC, and removing the PC removes it.
2. **The check code.** Both screens show `CheckCode()`, the first 6 characters of
   base32(sha256(device key ‖ approval key)). With a credential, its key takes the approval key's
   place, so the code covers the passkey the phone registers. It's tested on the vector the phones and
   KIFF pin (the public keys of private keys 1 and 2: `H6SJNU`, the second as an approval key or as
   the credential's; the first alone: `LTJFF6`). The phone asks whether the computer shows it, so a
   page showing its own QR can't get the phone to add someone else's key.
3. **The phone approves** with its approval key and the person's fingerprint (`POST /v1/me/pairings`,
   the phone's side).
4. **The claim.** `PairingClaim.PollAsync` posts to `/v1/pair/claim` with no auth, proving with the
   device key that it holds the key the QR showed (`kiff.pair.claim.v1`: nonce, sha256(key) hex). It
   polls every 3 s, adds 3 s on `slow_down`, and stops on expiry, `expired_token`, `invalid_proof`
   or the session.
5. **The session** (`PairedSession`) is handed over once. Its `ToString()` never shows the token, and
   an unreadable answer is never echoed, since a 200 holds the token.

## One attempt (`PairingAttempt`)

The page opens its device key in the TPM (`TpmKey.PairingKeyName`, made the first time) and asks Windows Hello for this
PC's passkey (`WindowsHello.CreateAsync`, ES256, the person verified), then shows the offer and claims it.

- **A new code keeps the passkey.** Show a new code makes a new nonce and reuses the passkey from the last
  attempt, so a retry asks for no new Windows Hello prompt and leaves no extra passkey in Windows' list.
- **The passkey's user id** is sha256 of the device key: the same on every attempt with that key.
- **With the creation proof on** (`Features.PairingPasskeyProof`), the challenge holds the nonce, so each
  attempt makes the passkey again. `PairingOffer.Create` then checks the proof's challenge is
  `PasskeyChallenge(nonce, pub)` and refuses a mismatch before the QR is shown, not at the claim.
- **No Windows Hello, or the person closes it:** the QR carries no credential. The PC then pairs as
  `passkey` without its own (it approves through the phone or the account's passkeys), and Added says so.
- **No TPM:** the screen says it wasn't added, in Windows' own words. A software-keyed view pairing
  isn't built.
- **For screenshots** (`--screenshot`), the page asks nothing of the TPM, Windows Hello or KIFF: its
  QR's key has no private half anywhere (a point found from a random x, not from a secret).

## The screen

`PairingScreen` says what the screen shows at each step; the app's `PairingPage` only draws it.

| Step | Shows |
| :- | :- |
| Waiting | the QR, the steps on the phone (its own menu names: Account, Devices, Add a computer), the check code, "Waiting for your phone…" |
| Expired | "This code expired." and Show a new code |
| Failed | why, as KIFF's status only (`KIFF answered 403`), and Show a new code |
| Offline | KIFF wasn't reached, and Show a new code |
| Added | added; or, with neither an approval key nor its own passkey, that approvals go through the phone or a passkey |

The QR is black on white in both themes, so any phone camera reads it. The code is counted down and
expires with the offer. A view-only session shows a notice above every screen, with Add this
Windows PC opening this screen.

## The kept session

A pairing must never leave a device at KIFF that this PC can't use: a relaunch would put the same key in a new QR, and KIFF
would refuse it as already registered. So, with [the session](session.md) kept across launches:
- **Before the code,** the store is checked (`CheckWritable`). A store that refuses stops the pairing with nothing sent.
- **Before Added,** the claimed session is kept, with its `approval_mode` (unknown is view) and the pairing key's name. If
  it isn't kept, the screen says it wasn't added, with Windows' error code only.
- **The view-only sign-in it replaces** is signed out at KIFF, and its key leaves the TPM.
- **A relaunch after pairing** opens this screen on Added (`PairingScreen.Opening`), never a new code.

**Not yet:**
- Closing Windows Hello's prompt when the attempt is stopped (`WebAuthNCancelCurrentOperation`).
- If the phone approved but the claim's answer was lost, Show a new code puts the same key in the QR, and KIFF refuses
  it. Removing this PC in Devices on the phone frees it.
