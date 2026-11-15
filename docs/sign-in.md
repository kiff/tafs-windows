# Signing this computer in

The same device authorization flow the phones use (KIFF's desktop client contract, §1.1). The core is
in `Tafs.Core/SignIn`, and the page is `SignInPage` in the app.

## The flow (`DeviceSignIn`)

1. **Start.** `StartBody` → `POST /v1/auth/device/start`, no auth:
   - the device key and the approval key, both standard base64 of SPKI DER;
   - the name, "tafs on this Windows PC" in the person's language;
   - `platform: windows`, `kind: other`.

   With no approval key, it sends `"approval": "passkey"` instead, and `"key_store": "software"` when
   the device key isn't in hardware. It checks the same rules as the pairing offer: P-256, two
   different keys, and a name of 1 to 64 characters.
2. **The code.** `ReadStart` returns the user code, the link and when it expires:
   - **The app opens only an `https` link on KIFF's own domain**: `kiff.dev`, or a host under it (the sign-in page is on `app.kiff.dev`), on the default port and with no user info. Anything else (`http`, `file:`, another app's scheme, `evilkiff.dev`) refuses the whole answer.
   - The device code is a secret until it's used, so it's never printed.
   - The interval is at least 5 s.
3. **The browser.** The person opens the link, signs in to KIFF, checks the code and approves.
4. **The session.** `PollAsync` → `POST /v1/auth/device/token` with the device code:
   - it waits the interval before each try, and adds 5 s on `slow_down`;
   - it stops on expiry, on `expired_token`, on a refusal, or with the session.

   The session is handed over once. Its `ToString()` never shows the token, and an unreadable answer is never echoed.

Once an account has an approval device, this session is **view-only**: it holds the device key but no
approval authority. The computer gains approval authority only by [pairing](pairing.md) with a device
that can already approve.

## The screen (`SignInScreen`)

| Step | Shows |
| :- | :- |
| Welcome | what tafs is, Sign in with KIFF, or "Already use tafs on your phone? Add this Windows PC with it." |
| Waiting | the code to check, Open KIFF to approve, and the link for another browser |
| Couldn't start / Couldn't finish | why, as KIFF's status only, and Sign in with KIFF again |
| Expired | the code expired, and Sign in with KIFF again |

Every line comes from the pinned words, and is tested in every shipped language. The tests run with
the `pairing` part.

## The page (`SignInPage`)

- **Start:**
  - it opens the device key in the TPM ([keys](keys.md)), or makes it the first time;
  - it asks for a passkey-only session, because on Windows the approval key is a Windows Hello passkey (`Features.WindowsPasskeyApprovals`). The button shows only with that flag on.
- **A refused start** shows KIFF's status and error code only (`KIFF answered 400 invalid_device_name`), never free text from the answer.
- **The code:** the browser opens on the link, the code is shown to check, and Open KIFF to approve opens it again.
- **The terms line** links tafs's Terms and Privacy policy (`kiff.dev/tafs/terms`, `/tafs/privacy`), as on the iPad.
- **Signed in:**
  - the window opens the Overview, with the view-only notice when `approval_mode` says view (or says nothing it knows);
  - **the session is kept** across launches, sealed for this Windows user ([the session](session.md)). A session it replaces is signed out at KIFF.
- **The link opened** is https on one of KIFF's own hosts, exactly: `kiff.dev`, `app.kiff.dev` or the API's.
- **Tests:** checks that create state never run against KIFF's production API. They run against a local or CI KIFF.
