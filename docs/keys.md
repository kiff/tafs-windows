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

## Canonical JSON for planque's digests

A grant's digest is the SHA-256 of what it covers in RFC 8785's canonical form (`Jcs`, ported from the iPad's `JCS`), so
the app can recompute it from the request it shows and approve exactly that request. It's used by the read of planque's
grants, in its own change.

- **Read here, not by System.Text.Json,** so each number keeps its literal and an integer is told from a fraction, as
  planque's Go does. A repeated key's last value wins, as in Go; anything that isn't JSON (a trailing comma, a lone
  surrogate, a control character in a string) is refused.
- **Written as RFC 8785 says:** keys by UTF-16 code units, numbers as ECMAScript prints them, strings with only the
  escapes they need.
- **An integer beyond 2^53 - 1 is refused,** as planque and KIFF refuse it: 2^53 and 2^53 + 1 would hash the same.
- **Tested** with planque's own `jcs_test.go` cases and the iPad's grant vector, which was made with planque's Go code.

## planque's grants, read as this device

What a taf asks to connect, as planque lists it (`PlanqueGrants`, ported from the iPad's `PlanqueClient.grants`
and `Grant`), read before anything is approved:

- **The read:** `GET /v1/accounts/{tenant}/grants` on planque, with the same `Planque-Device-Signature` as the
  proof above, in the app's language (`KiffHttp.PlanqueGetAsync`). It's shown in its own change, with the approval
  cards; this one reads and checks.
- **Only planque's exact host** (`PlanqueProof.IsPlanque`): https, `planque.kiff.dev`, the default port, no user. It
  carries the device's signature and never KIFF's token: `KeptSessions.MaySendTo` still lists only KIFF's API and
  tafs cloud.
- **Read as strictly as the iPad's decoder:** a grant without its id, status, words, digest, request (its server,
  agent and each tool's tool) or events, or with a field of another shape, fails the whole read. Health and
  actions are read leniently, as the iPad's.
- **The digest check:** each grant's `digest` must be the SHA-256 of what it covers, in RFC 8785's canonical form
  (`Jcs`, above), from the answer's own bytes, numbers as written:
  - with no `digest_v`, the request alone;
  - with `digest_v: 2`, `{"lang", "request", "text"}`: the words the person reads, and their language, are signed too;
  - any other version, or v2 without its words or language, covers nothing, so it never matches.
  `DigestMatches` is set here, never read from planque. Only a grant that matches will be approvable.
- **Each grant against its own bytes:** paired with its entry by its place in the list, never looked up by its id. A
  repeated id, or bytes listing another number of grants, fails the read: one request's digest never vouches for
  another's. (The iPad looks its entry up by id.)

## Setting planque up (`PlanqueEnrolment`)

Once per account, this device is enrolled with planque, which keeps the vendor sign-ins so they never reach this PC,
tafs cloud, the agent or KIFF. Ported from the iPad's `setUpPlanque` and `enrolPlanque`, with iOS #1106 (planque#248):

1. **Asked first with no key:** `POST /v1/accounts/{tenant}/enrolment` on planque with `{device_key}` (this device's SPKI
   DER, base64), signed `Planque-Device-Signature` (`PlanqueProof.SignAsync`, for exactly that method, path and body).
   A 2xx with `kept: true` means planque still holds a connector key KIFF takes: **done, and nothing is minted.** A
   device can't revoke a connector key, so a new one would only be left live in KIFF.
2. **Otherwise** (`needs_key`, a planque from before, any other answer, or not reached): **one** key is minted,
   `POST /v1/keys` on KIFF with `{"label":"planque","roles":["connector"]}`, signed as a setup call
   (`KIFF-Device-Signature`, `kiff.request.v1`, by the session's device key, which asks no one). It connects tools and
   does nothing else, and it's held only until it's handed to planque in a second enrolment, `{device_key,
   connector_key}`. It's never kept on this PC.
3. **What stops it:** KIFF's 403 on the mint says only an admin can connect apps; planque's 409 on the second enrolment
   says another device connects them; anything else says why ("Connecting apps isn't set up. …").

This change is the flow and its signatures only. Who may set planque up, and behind what, comes with its wiring:
a passkey session behind a Windows Hello assertion over `tafs.planque.v1|{tenant}` (`GateMessage`), a key session with
no prompt, like its other setup calls, and a view session never. It's held from merging until it has run once against a
real account.

The approval key isn't here. On Windows it's a Windows Hello passkey (see [approvals](approvals.md)).

## Tests

- **On Linux:** the TPM tests are skipped, and say why.
- **On Windows:** CI's `windows-keys` job runs them whenever pairing or approvals change. On a runner with no TPM, they say so in the log.
- **On a team PC:** they run against the real TPM, under keys with names of their own that are deleted afterwards. A test never touches the real device key.

```
dotnet test tests/Tafs.Core.Tests --filter "FullyQualifiedName~TpmKey"
```
