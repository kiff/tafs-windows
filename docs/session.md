# The kept session

This PC keeps its KIFF session across launches (`Tafs.Core.Kiff.KeptSession`, and the app's `SessionStore`). Without it,
closing the window would sign the PC out while its device stayed at KIFF.

- **Where:** the current Windows user's local app data, sealed with Windows' data protection for that user (DPAPI).
  Another Windows user, or the file copied to another PC, can't open it.
- **What:** the token, tenant, subject, device id (from a pairing), `approval_mode`, its expiry, and the name of the TPM
  key it's bound to. The token is never printed.
- **Reading it:**
  - only for KIFF's exact API host (`api.kiff.dev`), and only until it expires;
  - an unreadable one reads as none.
- **Where its token goes** (`KeptSessions.MaySendTo`): https only, on exactly `api.kiff.dev` or tafs cloud's
  `cloud.tafs.kiff.dev`, on the default port, with no user in the link. tafs cloud asks KIFF who the session is, and gets
  only reads (`GET /v1/home`). `KiffHttp.GetAsync` refuses any other link before sending. On a redirect, .NET drops the
  `Authorization` header, so the token doesn't follow it.
- **`approval_mode`:** `view`, `key` or `passkey`, from KIFF's answer. An unknown word, or none (`device_key`
  included), counts as view. The view-only notice shows exactly when the kept session says view.
- **Keeping one** (`KeptSessions.Replace`):
  - The session is written, then read back. A store that refuses throws, and the screen shows Windows' error code only,
    never a message that could hold a path.
  - `ISessionStore.CheckWritable` lets a flow stop before anything is sent.
- **Replacing one** (a view-only session by a paired one): the old session signs itself out at KIFF
  (`POST /v1/auth/device/logout`, bearer only, as the phones do), and its key leaves the TPM. If KIFF can't be reached,
  the old session ends with its 90 days, or from Devices on the phone. The same key's session again isn't signed out.
- **Screenshots** never read or write it.

**Tests** (the `pairing` part, `KeptSessionTests`):
- the modes;
- a relaunch reading the session;
- a replacement and what it signs out;
- a store that refuses;
- the exact host;
- the expiry;
- an unreadable store.
