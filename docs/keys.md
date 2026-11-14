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

The approval key isn't here. On Windows it's a Windows Hello passkey (see [approvals](approvals.md)).

## Tests

- **On Linux:** the TPM tests are skipped, and say why.
- **On Windows:** CI's `windows-keys` job runs them whenever pairing or approvals change. On a runner with no TPM, they say so in the log.
- **On a team PC:** they run against the real TPM, under keys with names of their own that are deleted afterwards. A test never touches the real device key.

```
dotnet test tests/Tafs.Core.Tests --filter "FullyQualifiedName~TpmKey"
```
