# The device key in the TPM

The device key signs with no prompt: setup calls, the pairing claim, and taps inside an approval window
(KIFF's desktop client contract, §4). On Windows it's made **inside the TPM**, through Windows' Platform
Crypto Provider (`TpmKey`, in `Tafs.Core/Kiff`).

- **P-256, signing only, not exportable.**
  - The private half never leaves the TPM. Windows can't export it, and this app never holds it.
  - A test checks that exporting it is refused.
- **One per Windows user**, named `tafs device key`.
  - `OpenOrCreate` opens it, or makes it the first time.
  - On opening, it checks the key is in the TPM and on P-256.
  - `Delete` removes it at sign out, and it can't come back.
- **Signatures:** SHA-256 of the message, signed in the TPM, as DER (never raw r‖s). KIFF's check
  verifies them, and the pairing claim's proof is tested with a TPM key.
- **No TPM:** `IsAvailable()` says so. The TPM refuses a key with no name (.NET asks for it to be
  exportable), so the check makes a named key and deletes it at once.

## planque's proof for a taf's Setup

The session's TPM key (`KeptSession.KeyName`: the device key for a sign-in, the paired key for a pairing) also signs
planque's read of the account's grants, which tafs cloud forwards with a taf's Setup so it can read its connected apps
(`PlanqueProof`, ported from the iPad's `PlanqueClient.grantsReadProof`). On the iPad it's the current key, the one
without access control, that signs it: this key's counterpart. It asks no one, and it grants nothing.
- **What's signed** (planque's `device.Message`): `planque.request.v1`, `https://planque.kiff.dev`, the tenant, `GET`,
  `/v1/accounts/{tenant}/grants`, the empty body's SHA-256 and the time, on seven lines with no trailing newline.
- **The time** (`SigningTime`, as the iPad's): RFC 3339 in UTC with milliseconds, never the same twice in this app.
- **The header:** `Planque-Device-Signature: t={time},v1={base64 DER signature},d={device}`. `d=` is this device's id in
  KIFF, always read from KIFF, as the iPad's `deviceLoader`: the one `GET /v1/me/devices` lists as `this_device` (`dev_…`),
  kept once found.
- **The key is opened, never made** (`TpmKey.Open`): a key gone with a sign-out throws, and Setup reads without the proof.
- **It goes only to tafs cloud** (`cloud.tafs.kiff.dev`), with the Setup read, through the exact host list.
- **Without it,** with no key, no account, no device, or a signature that fails, Setup reads without it, as the iPad's
  `try?`.

The approval key isn't here. On Windows it's a Windows Hello passkey (see [approvals](approvals.md)).

## Tests

- **On Linux:** the TPM tests are skipped, and say why.
- **On Windows:** CI's `windows-keys` job runs them whenever pairing or approvals change. On a runner with no TPM, they say so in the log.
- **On a team PC:** they run against the real TPM, under keys with names of their own that are deleted afterwards. A test never touches the real device key.

```
dotnet test tests/Tafs.Core.Tests --filter "FullyQualifiedName~TpmKey"
```
