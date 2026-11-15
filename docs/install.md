# Installing tafs for Windows (the team's MSIX)

Step 1 of the spec's download and install. The app comes as an MSIX for the team, **unsigned** until the signing
certificate is decided. Everyone else waits for the signed package.

## What the package is

- **One MSIX per platform:** x64 and ARM64. Each is self-contained: the Windows App SDK and .NET come inside it, so
  nothing else needs installing.
- **Its identity** (`src/Tafs.App/Package.appxmanifest`):
  - `KIFF.tafs`, shown as "tafs" by KIFF, with the mascot as its logos;
  - its publisher is `CN=tafs team build` with Windows' marker for an **unsigned package**
    (`OID.2.25.311729368913984317654407730594956997722=1`). That marker is what lets Windows install it without a
    signature, only when asked to, and never by a double-click.
- **Its version** is `0.1.<CI run number>.0`, so each build installs over the one before.
- **The usual build stays unpackaged.** The package is built only with `-p:TafsMsix=true`, and never signed here.

## Getting it

- **From CI:** the `msix` job builds it on every push to main, on a PR that changes the app, and on demand (Actions ›
  CI › Run workflow). Each run keeps `tafs-msix-x64` and `tafs-msix-ARM64` as artifacts, each a zip holding the `.msix`.
- **Building it yourself:**

  ```bash
  dotnet run --project tools/Tafs.Ci -- sync
  dotnet build src/Tafs.App -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:TafsMsix=true
  ```

  The package lands in `artifacts/msix/`.

## Installing, updating and removing

- **Install**, in PowerShell, as yourself (not as administrator):

  ```powershell
  Add-AppxPackage -Path .\Tafs.App_0.1.<n>.0_x64.msix -AllowUnsigned
  ```

  tafs then shows in Start like any app.
- **Update:** the same command with a newer package installs over it.
- **Remove:** Settings › Apps, or `Get-AppxPackage KIFF.tafs | Remove-AppxPackage`.
- **Your session:** the installed app keeps its own data, apart from a build run from a folder. So it may ask you to
  sign in or pair again once. The TPM key and the Windows Hello passkey are this PC's either way.

## Not yet

- **Signing.** It waits on the certificate decision. Once signed, the publisher becomes the certificate's subject and
  `-AllowUnsigned` goes.
- **Updates on their own** (an App Installer file) and **a download page.**
- **Installed on a PC here:** the package is built and its contents checked (its identity, the app, the words, the
  logos), but it hasn't been installed and launched yet.
