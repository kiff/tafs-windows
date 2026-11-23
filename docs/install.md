# Getting tafs for Windows

Step 1 of the spec's download and install. People get tafs from the **setup** (`tafs-setup-<version>-x64.exe` and
`-arm64.exe`), in their PC's language, from kiff.dev's download page once a signed release is published. Until then:
- the owner tests a **draft** release, unsigned and marked never publish (Releasing, below);
- the team runs CI's builds, the setup or the **zip** (unzip and run, with nothing installed).

The **MSIX** is built too, for the day it's signed.

## The zip (the team's quick build)

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

## The installer (what people download)

- **What it is:** `tafs-setup-<version>-x64.exe` and `tafs-setup-<version>-arm64.exe`, WiX's standard setup around one
  MSI per platform (`installer/Bundle.wxs`, `installer/Package.wxs`). It installs the same self-contained publish as the
  zip, for every user of the PC, under `Program Files\tafs`, puts tafs in Start, and offers **Open tafs** on its last
  page. It asks for administrator rights once. A newer version replaces an older one in place; uninstalling (Settings ›
  Apps) removes the files, never what the app keeps for a person in `%LOCALAPPDATA%\tafs`.
- **Windows 10, version 1809 (build 17763) or later,** with a TPM to sign in (the session's key lives there) and Windows
  Hello to approve.
- **Built by CI** in the zip job (`ci/installer.sh`), every run that builds the zip, with WiX v5.0.2 pinned in `.config/dotnet-tools.json`
  and its standard bootstrapper extension at the same version. On x64, CI installs it quietly, starts tafs from where it
  put it (its sign-in screen saved, or the job fails), and uninstalls it. Kept from main, the nightly run and on demand
  as `tafs-windows-setup-x64` and `tafs-windows-setup-ARM64` (the setup .exe and its MSI).
- **Building it yourself,** after the zip's publish, in Git Bash from the repository's root:

  ```bash
  export PLATFORM=x64 VERSION=0.1.0.0 SETUP_NAME=0.1.0.0
  bash ci/installer.sh tools && bash ci/installer.sh msi && bash ci/installer.sh bundle   # artifacts/installer/tafs-setup-0.1.0.0-x64.exe
  ```
- **Its icon** is the mascot's (`src/Tafs.App/Assets/tafs.ico`): the app's, in Start and the taskbar, and the setup's and
  Windows' installed-apps list.
- **Signed** in CI's `sign` job (Signing, below): the app's files inside it, the MSI, the setup's engine and the setup.
- **Its words are in 8 of the app's languages** (en, de, fr, es, it, pt, nl, pl), in the PC's display language, with English as the fallback. The app's other 35 aren't in the setup yet.
  - **Where they live:** `installer/words/<lang>.wxl` holds every line of WiX's standard setup (64), translated with the app's own lines. WiX v5 ships these screens in English only.
  - **How the setup picks one:** it looks for `<LCID>\thm.wxl` for each of the person's preferred UI languages, then for their language's default variant (fr-BE finds fr-FR's). Spanish is held under 3082 and 1034, and Portuguese under 2070 and 1046. Burn's `/lang <LCID>` chooses one by hand.
  - **What's checked:**
    - `SetupWordsTests`: each language has exactly English's ids, keeps each line's placeholders, link and key shortcut, and its buttons fit;
    - `ci/installer.sh`: every LCID is read back out of the setup it built;
    - on x64, CI keeps `setup-screens`, the first and last screens in French and German, from the real setup with `/lang` (`ci/setup-screens.ps1`).
- **No Options page:** the MSI always installs under `Program Files\tafs`, so a folder chosen there would have been ignored.

## Releasing (a draft; the owner publishes)

- **What kiff.dev's download page shows:** this repository's latest published release (not a draft, not a prerelease)
  that carries an x64 setup named `*-x64.exe`. Until there is one, Windows is "Coming soon" there.
- **Making one:** Actions › CI › Run workflow on `main`, with **release** set to the version, such as `1.0.0`, **part**
  left empty, and **sign** ticked for a build that's meant to be published. A release always runs the full suite,
  unfiltered, as the nightly run (a part with it is refused), with the app and its setups at `<release>.<run>`. Its
  `release` job runs only when every check of that run passed, none skipped (the tests, the app, the TPM keys, and the
  zip's install), and, with **sign** ticked, only when signing passed. It makes a **draft** release, tagged `v1.0.0` at
  that commit. A draft never shows on the page. Another run for the same version makes another draft beside the last
  (delete the one not wanted), and its setup installs over the last one in place.
- **Not from a commit that changes `.github/`:** GitHub refuses the run's token a release there (a 403), so `plan`
  refuses it first. Main's next commit releases fine.
- **Signed** (sign ticked): the draft carries the signed setups, `tafs-setup-1.0.0-x64.exe` and `-arm64.exe`, with each
  one's SHA-256 in its notes.
- **Unsigned** (sign not ticked, for the owner's install test before signing exists): the setups are named
  `tafs-setup-1.0.0-x64-UNSIGNED.exe` and `-arm64-UNSIGNED.exe`, so the page, which takes only `*-x64.exe`, never serves
  them, and the draft's title and notes say **never publish**. Its notes carry the SHA-256s too.
- **Its notes:** each version's are in `release-notes/v<version>.md`, in the setup's 8 languages and v1.0.0's order
  (English, Deutsch, Español, Français, Italiano, Nederlands, Polski, Português). They're put on the draft by hand,
  above the SHA-256 lines and the run that CI writes there, so the hashes stay CI's own.
- **Publishing it:** the owner presses Publish, as for every computer, on a **signed** draft only, once their test of
  that build passed. The team prepares the draft and says it's ready; no one else publishes. The page shows it within
  10 minutes.
- **The team's own builds** stay at `0.1.<run>.0`, below a release, so a team setup won't install over a released one.

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

- **What's signed,** in this order, with KIFF's Azure Artifact Signing certificate, SHA-256 and Microsoft's timestamp:
  the app's own files (`Tafs.App.exe`, `Tafs.App.dll`, `Tafs.Core.dll`), then the MSI built from those signed files,
  then the setup's engine (detached from the setup, signed and put back, as WiX requires), then the setup itself. The
  MSIX joins once it's built for it.
- **For real only on demand:** Actions › CI › Run workflow on `main`, with **sign** ticked. That runs CI's `sign` job in
  the repository's `windows-signing` environment, so a push to main never waits on the environment's protection rules.
  **Only the owner approves a `windows-signing` deployment, in the UI;** no session does.
- **No secret:** the job signs in to Azure by OIDC (`azure/login`), as an app registration whose federated credential's
  subject is exactly `repo:kiff@2176846/tafs-windows@1458016196:environment:windows-signing`, so only a run in that
  environment can sign. That's GitHub's immutable form, which this organisation uses: the owner and the repository by
  name and id (`GET repos/kiff/tafs-windows/actions/oidc/customization/sub` gives the prefix), so a renamed or re-made
  repository can't take its place. No long-lived secret exists. `id-token: write` is on the `sign` job only. Everything the job downloads (the app,
  WiX and its extension) comes before it signs in, and its checkout keeps no token. The signing action then uses that
  sign-in alone.
- **Its values,** all variables of the environment: `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `WINDOWS_SIGNING_ENDPOINT`,
  `WINDOWS_SIGNING_ACCOUNT`, `WINDOWS_SIGNING_PROFILE` and `WINDOWS_SIGNING_SUBJECT` (the certificate's full subject, as
  Windows shows it). Any value missing fails the run, naming it.
- **Checked:** after each signing, each signature must be Valid and by exactly that subject (`ci/check-signatures.ps1`),
  so a misconfigured profile, or another organisation's certificate, fails even when it chains to a trusted root. The
  signed x64 setup must then install, start and uninstall. It keeps `tafs-windows-signed-x64` and
  `tafs-windows-signed-ARM64` (the app), and `tafs-windows-signed-setup-x64` and `tafs-windows-signed-setup-ARM64` (the
  setup and its MSI).
- **The dry run, on every main push and on demand** (`sign-dry-run`, no environment): the app's files signed with a
  throwaway self-signed certificate made in that run (`ci/sign-dry-run.ps1`), then read back by the same check on that
  certificate's subject (untrusted by design). It proves the file list, `signtool` and the check, and keeps nothing.
- The actions are Azure's own: `azure/login`, pinned to v3.1.0's commit, and `Azure/artifact-signing-action`, pinned to
  v2.0.0's commit (in `.github/actions/sign`).

## Not yet

- **Signing the MSIX,** with the same job: it needs its publisher set to the certificate's subject first.
- **Updates on their own** (an App Installer file). Until then the app tells, and only tells, when a newer release is out (docs/app.md, A new version).
