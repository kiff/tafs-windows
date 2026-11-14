# Signing this computer in

The same device authorization flow the phones use (KIFF's desktop client contract, §1.1). This page
covers the portable core in `Tafs.Core/SignIn`. The screen follows once the device key is in the TPM,
because a start sends that key.

## The flow (`DeviceSignIn`)

1. **Start.** `StartBody` → `POST /v1/auth/device/start`, no auth:
   - the device key and the approval key, both standard base64 of SPKI DER;
   - the name, "tafs on this Windows PC" in the person's language;
   - `platform: windows`, `kind: other`.

   With no approval key, it sends `"approval": "passkey"` instead, and `"key_store": "software"` when
   the device key isn't in hardware. It checks the same rules as the pairing offer: P-256, two
   different keys, and a name of 1 to 64 characters.
2. **The code.** `ReadStart` returns the user code, the link and when it expires:
   - **The app opens only an `https` link.** Anything else in the answer (`http`, `file:`, another app's scheme) and the answer is refused.
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
