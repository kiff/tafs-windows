# Getting tafs for Windows

Step 1 of the spec's download and install. Until a signing certificate is decided, the team runs the app from a **zip**:
unzip it and run it, with nothing installed and no Windows setting changed. The **MSIX** is built too, for the day it's
signed. Everyone else waits for the signed package.

## The zip (what the team runs now)

- **What it is:** the app as it builds, unpackaged and self-contained. .NET and the Windows App SDK come inside it, so
  nothing else needs installing. There's one for x64 and one for ARM64 (about 165 MB unzipped).
- **Getting it:** CI's `zip` job builds it on every push to main, on a PR that changes the app, and on demand (Actions ›
  CI › Run workflow). Each run keeps `tafs-windows-x64` and `tafs-windows-ARM64`; each downloads as a zip.
- **Running it:**
  1. Before unzipping, open the zip's Properties and tick **Unblock**, if Windows shows it. Otherwise Windows may stop
     the app at first with "Windows protected your PC": choose More info › Run anyway. The app isn't signed yet.
  2. Unzip it anywhere you can write, such as a folder in Documents.
  3. Run `Tafs.App.exe`. Pin it to Start or the taskbar if you like.
- **Updating:** unzip the newer one in place of the old folder.
- **Removing:** delete the folder. What the app keeps (the session, the theme) is in `%LOCALAPPDATA%\tafs`; delete that
  too to forget them. The TPM key and the Windows Hello passkey are this PC's, and signing out removes them.
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

## Not yet

- **Signing,** of the MSIX and the app itself. It waits on the certificate decision.
- **Updates on their own** (an App Installer file) and **a download page.**
