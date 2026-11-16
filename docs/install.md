# Getting tafs for Windows

Step 1 of the spec's download and install. Until a signing certificate is decided, the team runs the app from a **zip**:
unzip it and run it, with nothing installed and no Windows setting changed. The **MSIX** is built too, for the day it's
signed. Everyone else waits for the signed package.

## The zip (what the team runs now)

- **What it is:** the app as it builds, unpackaged and self-contained. .NET and the Windows App SDK come inside it, so
  nothing else needs installing. There's one for x64 and one for ARM64 (about 165 MB unzipped).
- **Getting it, only from main:** Actions › CI, filtered to branch `main`, from a run whose `test` and `app` jobs passed
  (the nightly run, a push to main, or Run workflow on main). Each such run keeps `tafs-windows-x64` and
  `tafs-windows-ARM64`, each a zip, stamped with the run's version (`0.1.<run number>.0`) and its commit.
- **Never run anything from a PR's run.** A PR that changes the app only checks that it publishes and starts; it keeps no
  download. A PR from a fork runs its own code.
- **Running it:**
  1. Before unzipping, open the zip's Properties and tick **Unblock**, if Windows shows it. Otherwise Windows may stop
     the app at first with "Windows protected your PC": choose More info › Run anyway. The app isn't signed yet.
  2. Unzip it anywhere you can write, such as a folder in Documents.
  3. Run `Tafs.App.exe`. Pin it to Start or the taskbar if you like.
- **Updating:** unzip the newer one in place of the old folder.
- **Removing:** delete the folder. What the app keeps (the session, the theme) is in `%LOCALAPPDATA%\tafs`; delete that
  too to forget them. Sign out first: that removes the session's key from the TPM. A Windows Hello passkey made for
  kiff.dev stays with Windows Hello.
- **Building it yourself:**

  ```bash
  dotnet run --project tools/Tafs.Ci -- sync
  dotnet publish src/Tafs.App -c Release -p:Platform=x64 -r win-x64 --self-contained -o artifacts/zip/tafs-x64
  ```

## The MSIX (once it's signed)

- **What it is:** `KIFF.tafs`, shown as "tafs" by KIFF, self-contained, one package per platform, with logos made from
  the mascot (`src/Tafs.App/Package.appxmanifest`). It's built only with `-p:TafsMsix=true`; every other build stays
  unpackaged.
- **Why it isn't the download yet:** Windows installs no unsigned package that holds an app. `Add-AppxPackage
  -AllowUnsigned` refuses it with 0x80073D2B ("an unsigned package can't contain executable activations"), tried on
  15 Nov. Until it's signed, its publisher carries Windows' marker for an unsigned package
  (`OID.2.25.311729368913984317654407730594956997722=1`). Signing will replace that with the certificate's subject.
- **Getting it:** CI's `msix` job keeps `tafs-msix-x64` and `tafs-msix-ARM64`. Each build's version is
  `0.1.<run number>.0`, so a signed one installs over the last.

## Signing (wired, off until Azure exists)

- **What's signed:** the app's own files in each zip (`Tafs.App.exe`, `Tafs.App.dll`, `Tafs.Core.dll`), with KIFF's
  Azure Artifact Signing certificate, SHA-256 and Microsoft's timestamp. The installer and the MSIX join it once they're
  built for it.
- **For real only on demand:** Actions › CI › Run workflow on `main`, with **sign** ticked. That runs CI's `sign` job in
  the repository's `windows-signing` environment, so a push to main never waits on the environment's protection rules,
  which are the owner's call. Its values: the variables `WINDOWS_SIGNING_ENDPOINT`, `WINDOWS_SIGNING_ACCOUNT`,
  `WINDOWS_SIGNING_PROFILE` and `WINDOWS_SIGNING_SUBJECT` (the certificate's full subject, as Windows shows it), and the
  secrets `AZURE_TENANT_ID`, `AZURE_CLIENT_ID` and `AZURE_CLIENT_SECRET` (an app registration allowed to sign with that
  profile). Any value missing fails the run, naming it. After signing, each file's signature must be Valid and by exactly
  that subject (`ci/check-signatures.ps1`), so a misconfigured profile, or another organisation's certificate, fails even
  when it chains to a trusted root. It keeps `tafs-windows-signed-x64` and `tafs-windows-signed-ARM64`.
- **The dry run, on every main push and on demand** (`sign-dry-run`, no environment): the same files signed with a
  throwaway self-signed certificate made in that run (`ci/sign-dry-run.ps1`), then read back by the same check on that
  certificate's subject (untrusted by design). It proves the file list, `signtool` and the check, and keeps nothing.
- The action is Azure's own (`Azure/artifact-signing-action`), pinned to v2.0.0's commit.

## Not yet

- **Signing the MSIX and the installer,** with the same job: the MSIX needs its publisher set to the certificate's subject
  first, and the installer comes with WiX.
- **Updates on their own** (an App Installer file) and **a download page.**
