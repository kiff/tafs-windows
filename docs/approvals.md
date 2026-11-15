# Approving on this computer

Every approval signs the exact action. On Windows the signature comes from **Windows Hello as a
platform passkey** for kiff.dev: an ES256 (P-256) key in the TPM, and Windows itself asks for the
person (face, fingerprint or PIN) on **every** assertion. The app never holds the private key and
never decides on its own that the person was asked.

Why not a TPM key behind a prompt the app shows: the prompt would be the app's, so other software
running as the person could sign with the key without it. Windows Hello's own key store is RSA only,
and KIFF takes P-256 only.

**On** (`Features.WindowsPasskeyApprovals`) since KIFF accepts a computer's own passkey:
- a Windows sign-in asks for a passkey-only session (`"approval": "passkey"`), and KIFF refuses a Windows start with neither approval key nor that flag;
- a passkey session approves only with `KIFF-Passkey-Assertion`.

The Windows WebAuthn calls are built (`WindowsHello`, below). A session paired with this PC's passkey keeps its credential
id (`KeptSession.PasskeyId`). Two things assert with it: starting Coach, which signs Setup's draft with it (`coach.md`),
and setting planque up. Until approving is wired, this PC asserts with it for nothing else, so its approvals go to the
phone. Switched off, `PasskeyApprover` refuses before Windows Hello is asked.

## An approval (`PasskeyApprover`)

1. Build the request's `kiff.request.v1` message, as a device key would sign it.
2. `clientDataJSON` = `{"type":"webauthn.get","challenge":<base64url(sha256(message))>,
   "origin":"https://kiff.dev","crossOrigin":false}`. The challenge binds the assertion to this one
   request. The origin isn't a security boundary for a native app: the challenge is.
3. Windows (the WebAuthn API) asks for Windows Hello and returns authenticator data and a DER signature
   over authData ‖ sha256(clientDataJSON).
4. **Before sending,** the app refuses what KIFF would refuse: an rpIdHash that isn't sha256("kiff.dev"),
   no user presence or no user verification, and a passkey that is backup eligible or backed up (it can
   leave this computer, so it isn't this computer's own).
5. The header: `KIFF-Passkey-Assertion: t=<signed_at>,a=<base64url of {credential_id,
   client_data_json, authenticator_data, signature}>`, each field base64url.

`signCount` is never relied on: Windows may keep it at 0. KIFF records each request as used.

## Registering (`PasskeyRegistration`)

Reads a new passkey's authenticator data: the credential id and its ES256 COSE key, turned into the
SPKI DER KIFF stores. It refuses any other algorithm or curve, a point off the curve, another relying
party, and the flags above.

## The Windows calls (`WindowsHello`)

`webauthn.dll`, Windows' WebAuthn API. Windows draws its own prompt every time, and the private key never
leaves the TPM.

- **`IsAvailable()`:** the API is there and a platform authenticator that verifies the person. It never
  shows anything.
- **`CreateAsync(userId, userName, displayName)`:** makes this PC's passkey for kiff.dev.
  - The options: ES256 (`-7`) only, on this PC's platform authenticator (no phone, no security key),
    `userVerification` required, attestation `none`. The challenge is random, and nothing is kept: the
    pairing's check code and the phone's approval are what bind the credential to this PC.
  - The caller passes the user fields: the account's subject and email when it's signed in, otherwise
    random bytes and this PC's name.
  - The authenticator data goes through `PasskeyRegistration.Read`, so a passkey that could leave this
    PC (BE or BS set), another relying party, or another algorithm is refused before it's offered.
- **`GetAssertionAsync`** is `IPasskeyAuthenticator` for `PasskeyApprover`. It's one assertion with this
  PC's credential only, user verification required, over exactly the `clientDataJSON` this app built
  (`WEBAUTHN_CLIENT_DATA` with SHA-256). Windows hashes those bytes as they are, so the challenge, and the
  request it approves, are this app's.
- **Errors:**
  - Closing Windows Hello (`ERROR_CANCELLED`, `NTE_USER_CANCELLED`) is a cancel.
  - Anything else is refused with Windows' own name for the error.

**The creation proof, built and switched off** (`Features.PairingPasskeyProof`, 15 Nov):
- The QR's credential ships as it is.
- When KIFF takes the proof, the pairing's passkey is made with the challenge
  `PairingClaim.PasskeyChallenge`: base64url(sha256 of `kiff.passkey.pair.v1`, the nonce and pub, joined
  by newlines, as the QR writes them).
- The claim then also sends `credential_proof: {client_data_json, attestation_object}`, as Windows
  returned them, base64url. Off, the claim is unchanged.

**Not decided yet:**
- `residentKey: preferred` needs the options' version 4. Version 1 asks for a non-discoverable
  credential, which "preferred" allows.

**Tests:**
- Each native structure is pinned at `webauthn.h`'s x64 offsets (`WindowsHelloLayoutTests`), on CI's
  Windows job, beside the TPM key.
- Windows Hello's prompt itself needs a person. It's checked by hand on a PC with Windows Hello: make a
  passkey, then assert with it. It's never run on CI.

## A taf's Cards before they're confirmed (`KiffDrafts`)

What a draft would issue, read from KIFF as the session (`GET /v1/me/agent-drafts/{id}`), as the iPad's
`KIFFClient.draft`, `AgentDraft` and `DraftCard`. It's drawn on Coach's approval cards in their own change; nothing
here confirms anything.

- **Read as strictly as the iPad's decoder:** its id, agent, status and version are required; each Card, grant, cap
  and total in its shape, or the draft isn't shown. A claim secret is checked for its shape and never kept.
- **A scale past 18** on a cap or a total (`KiffAmount.MaxScale`), where the figure would overflow: the draft isn't
  shown, so a Card is never confirmed with an amount the person couldn't read, and it never crashes.
- **The lines the person reads** (`DraftCard.Lines`):
  - KIFF's own lines, when it sends them;
  - otherwise each action by its title ("Refund a payment: up to €100 each"), then the totals in KIFF's window
    ("€1,000 in total a day", "20 calls a day"); an amount's unit is KIFF's, or it reads "(no currency)";
  - on a Card that asks past its limits: "; more asks you first", ", then it asks you", and a count of 0 is
    "Each call asks you first";
  - two grants that read alike, on one Card or two, each add their action in words.
- **The Card's name:** the name the draft was submitted with ("Refunds assistant's Card"), never its agent id; else
  "The taf's Card".

## What a Windows passkey never does

Any program on the computer can ask Windows for a kiff.dev assertion, and Windows' prompt doesn't show
what is being approved. So **this passkey never counts as the second, independent device**: payments
and new money connections still need the phone. A Windows passkey alone never moves money.

## Tests

The `approvals` part checks the header the way KIFF's verifier does, step by step, and every refusal
above with a negative test. The Windows WebAuthn calls themselves come in the app, behind the same flag.
