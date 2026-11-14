# Approving on this computer

Every approval signs the exact action. On Windows the signature comes from **Windows Hello as a
platform passkey** for kiff.dev: an ES256 (P-256) key in the TPM, and Windows itself asks for the
person (face, fingerprint or PIN) on **every** assertion. The app never holds the private key and
never decides on its own that the person was asked.

Why not a TPM key behind a prompt the app shows: the prompt would be the app's, so other software
running as the person could sign with the key without it. Windows Hello's own key store is RSA only,
and KIFF takes P-256 only.

**Off for now** (`Features.WindowsPasskeyApprovals`), until KIFF registers a computer's own passkey
when the computer is paired. Until then every approval goes to the phone.

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

## What a Windows passkey never does

Any program on the computer can ask Windows for a kiff.dev assertion, and Windows' prompt doesn't show
what is being approved. So **this passkey never counts as the second, independent device**: payments
and new money connections still need the phone. A Windows passkey alone never moves money.

## Tests

The `approvals` part checks the header the way KIFF's verifier does, step by step, and every refusal
above with a negative test. The Windows WebAuthn calls themselves come in the app, behind the same flag.
