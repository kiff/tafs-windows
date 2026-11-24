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
- a passkey session approves only with its passkey: a request with `KIFF-Passkey-Assertion`, and a held call's answer
  (approve once, extra room) with its assertion in the body, `"signature": {"signed_at", "passkey": {credential_id,
  client_data_json, authenticator_data, signature}}`, its challenge base64url(sha256) of that answer's `kiff.answer.v1`.

**Where this PC asks less than the iPad:** Windows Hello takes the PC's PIN as well as a face or a fingerprint, where the
iPad's approvals that grant (`grants: true`) take biometrics only. Windows' platform passkey has no biometrics-only mode;
the PIN is Windows Hello's own fallback, bound to this PC's TPM.

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

**The creation proof, on** (`Features.PairingPasskeyProof`, built 15 Nov, on 16 Nov). KIFF refuses a passkey
computer's pairing claim without it from 1 Dec 2026, and never takes a bad proof as none:
- The pairing's passkey is made with the challenge `PairingClaim.PasskeyChallenge`: base64url(sha256 of
  `kiff.passkey.pair.v1`, the nonce and pub, joined by newlines, as the QR writes them). So each new code makes the
  passkey again.
- The claim also sends `credential_proof: {client_data_json, attestation_object}`, base64url:
  - `client_data_json` as Windows hashed it: `webauthn.create`, the challenge, `https://kiff.dev`, `crossOrigin` false;
  - `attestation_object` built here from the create's authenticator data, `{fmt: "none", attStmt: {}, authData}`
    (`PasskeyCreationProof.WithNoAttestation`), as WebAuthn's "none" conveyance writes it. KIFF takes only "none" and
    reads the key from authData, so whatever statement Windows' own object carries is never sent.
- KIFF also checks UP and UV, one ES256 key, and that the id and key are the ones the phone approved.

**Not decided yet:**
- `residentKey: preferred` needs the options' version 4. Version 1 asks for a non-discoverable
  credential, which "preferred" allows.

**Tests:**
- Each native structure is pinned at `webauthn.h`'s x64 offsets (`WindowsHelloLayoutTests`), on CI's
  Windows job, beside the TPM key.
- Windows Hello's prompt itself needs a person. It's checked by hand on a PC with Windows Hello: make a
  passkey, then assert with it. It's never run on CI.

## Windows Hello not ready (`HelloState`, `HelloGate`, `ApprovalGate`)

As the iPad's `ApprovalSetupView`: an approval's button, or, while Windows Hello can't confirm here, why and what to do
in its place, so the person learns it before the prompt, not from a failure after it.
- **One value for the app** (`HelloState`): Windows' `UserConsentVerifier.CheckAvailabilityAsync`, asked whenever the
  window is activated (at launch, and coming back from Settings), and on Check again, which answers for every gate at
  once. Ready until asked; a Windows that doesn't say leaves each approval to say what came of it. A sign-out forgets it.
- **The words** (`HelloGate.NotReady`), the iPad's `ApprovalReadiness.short` with Windows Hello for Face ID or Touch ID:
  - not set up: "Approving needs Windows Hello, and none is set up. Set it up in Settings → Accounts → Sign-in
    options." (`approval.settings_path.windows`, Windows 11's own names in each language); with **Open Settings**, which
    opens that very page (`ms-settings:signinoptions`), and **Check again**;
  - busy: "Windows Hello can't be used right now. Try again in a moment.", with Check again;
  - no Windows Hello it can use: "This device can't approve. Approve on another device signed in to your account.", alone.
- **Where** (`ApprovalGate.Wrap`, in a button's place, the iPad's `ApprovalGate`): Needs you's Approve once and Extra
  room, Allow from now on's Approve, Coach's Start, a halt's Resume, and Make live. A button that's hidden stays hidden.
- **In a dialog** (`ApprovalGate.Notice`, the iPad's `ApprovalNotice`): the line and its buttons above the dialog's
  content, and its approving button off until a check finds Windows Hello ready. Connect, Give the key, an app event's
  Approve, Install this version (a version's and a trial's), Share, Coach's tools, Remove for good, and Devices' Sign
  out and Remove.
- **Your step** keeps its own line ("…to answer here, or answer on your phone"), from the same value.
- **Nothing moves mid-approval.** Windows Hello's prompt is its own window, so tafs's window is activated again when it
  closes, in the middle of the approval that asked for it. A check then redraws only if the answer changed. And a
  dialog's notice only ever turns its button off, back on only where it turned it off itself, so a dialog busy approving
  (its button off while it signs) is never turned back on. Dialogs with their own redraw keep their busy state in it
  (Share's `sharingNow`, an app event's `signing`, Connect's and the key's `busy`).

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

## Raise the limit and Decline: Windows Hello's passkey, then the device key

KIFF takes these two only signed by the device key (`KIFF-Device-Signature`, its signed routes), never
as a passkey's approval. A device key that signs with no prompt proves no one is there, so it signs
only after this PC's Windows Hello passkey checked the person, for that very request
(`Tafs.Core/Usage/DeviceSigned.cs`):
- **The request first, fixed.** Raise (`PUT /v1/me/usage/limit`, `{"raise":true,"from_cents":N}`) or
  Decline (`POST /v1/me/pack-requests/{id}/decline`, `{"note"}`) is made once, at a new `signed_at`,
  before Windows Hello is asked, and never changes until it's sent (`DeviceSignedRequest`). Its note is
  the one typed when Decline was pressed, and the field is off while Windows Hello is up; Raise's
  figure on the sheet is that request's `from_cents`.
- **What the passkey answers over.** `tafs.presence.request.v1`: the tenant, `signed_at`, and the
  SHA-256 of the request's `kiff.request.v1`. It's never sent anywhere, and its first line keeps it
  from ever being a message KIFF takes as an approval. Windows Hello is asked for this session's
  passkey alone (`allowCredentials`), with user verification required, and its title names the act.
- **The answer, checked here.** As KIFF checks one (kiff.dev's, present and verified, never a passkey
  that can leave this PC), from the credential asked (an answer naming another is refused; one
  naming none is the one asked), and by its signature against the passkey's public key kept when
  this PC paired (`KeptSession.PasskeyKey`), never a key from the answer. Only that makes a
  `Presence`: bound to that request, spent on use (a mismatch spends it too), and too old once its
  `signed_at` is past KIFF's window (60 s).
- **Then the device key, then KIFF.** The session's TPM key signs the request's `kiff.request.v1`,
  `t=<signed_at>,v1=<base64 DER>`, through the `Presence` alone, and the request goes to KIFF once.
  Tests pin the order by what's called: no signature before a checked presence, no send before the
  signature.
- **Closed or refused.** Windows Hello closed: nothing is sent, and the sheet stays to try again. Not
  confirmed (Windows Hello failed, or the check didn't hold): nothing is sent, "Not sent: it wasn't
  confirmed." KIFF's `409 limit_changed` and `limit_ceiling` are said in the app's words; anything
  else is "The limit wasn't raised. {why}" or "The request wasn't declined. {why}". A sign-out
  meanwhile stops it quietly.
- **Who can.** A passkey session that kept its passkey's public key, with its TPM key. A session
  paired before the key was kept, a key or view session, or one without its TPM key, keeps the
  can't-approve line and sends nothing; pairing again gives it the key.

## What a Windows passkey never does

Any program on the computer can ask Windows for a kiff.dev assertion, and Windows' prompt doesn't show
what is being approved. So **this passkey never counts as the second, independent device**: payments
and new money connections still need the phone. Raise the limit and Decline, above, are the one
exception, as on Linux: KIFF takes them signed by this computer's own device key, and the passkey is
only this computer's check that the person is there for that request.

## Tests

The `approvals` part checks the header the way KIFF's verifier does, step by step, and every refusal
above with a negative test. The Windows WebAuthn calls themselves come in the app, behind the same flag.
