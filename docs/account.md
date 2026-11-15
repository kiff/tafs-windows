# Account

Account is the iPad's `SettingsView`, as in the reference set's `account.png`. The core is
`Tafs.Core.Account.AccountScreen`; `AccountPage` builds it from rows in the grouped list's style (see
[connect apps](connect-apps.md)).

The sections, in the iPad's order:
- **Your account:** Mode, as Sample data, Signed in with KIFF, or Signed out.
  - **View only**, with Add this Windows PC, shows exactly when the kept session's `approval_mode` says view ([the session](session.md)).
- **Alerts:** a row that opens. The alerts themselves aren't built yet.
- **Theme:** Light, Dark or System, the iPad's segmented control. It sets the window's theme, and it's kept for the
  next launch (`ThemeChoices`), as the iPad keeps `tafs.theme`.
  - **Where:** one word (`light`, `dark` or `system`) in `%LOCALAPPDATA%\tafs\theme`, beside the session. It's written
    beside the file and moved over it, so it's whole or not there.
  - **What can't be read** is the system's. A theme that can't be kept still shows now.
  - **At launch,** `--theme light|dark` wins, for CI's screenshots. A screenshot never reads what this PC kept, so the
    same command gives the same picture anywhere.
- **Language:** the app's language, by its own name ("Français"), and Change language. That opens Windows' language
  settings, which the app follows, as the iPad opens iOS's.
- **How it works:** the sign-in's line, for this Windows PC.
- **Legal:** Terms and Privacy policy. They open tafs's pages, which are in English only for now, so outside English each
  row says so ("Conditions (en anglais)", from `settings.legal_in_english`).
- **About:** Version, "1.0 (1)" from the app's version, and the tagline.
- **Account actions:**
  - over the sample: Sign in with KIFF, and Clear the sample in red, which starts the app again without it;
  - signed in: **Sign out**, in red, asked first ("Sign out of KIFF?", with what it does to this Windows PC). Signing out
    ends the session at KIFF (`POST /v1/auth/device/logout`), forgets the kept session, and removes its key from the TPM,
    whatever the store did (in a `finally`). Then everything read as that session goes, as the iPad's `forgetSession`
    (`MainWindow.ForgetSession`, the same reset a new sign-in runs first): Home's list on the Overview and under Your
    tafs, the pages' reads and Setup's proof, this device's id, the Record's lines and its KIFF read, and Coach's
    thread, answers, sending, and the grants and drafts its approvals read. A read the
    session started that lands afterwards is dropped. The window then opens sign-in. If Windows refuses to forget the session, it
    says "Sign out didn't go through" with Windows' error code only, and stays here to try again.
  - signed out: Sign in with KIFF.

## Alerts (`AlertPrefs`, `AlertsScreen`, `AlertsPage`)

Ported from the iPad's `NotificationSettingsView` and `LockScreenAlert`, for this PC's own alerts (the notifications a
held call raises while tafs runs, `needs-you.md`). tafs-macos #68 is a second reference.
- **What an alert looks like:** the iPad's two, on the raised colour with corners of 18. Each has the mascot, its title
  and line, and the sound mark: "Chargebacks needs you" with sound, and "Refunds answered maria@…" silent. Narrator reads
  "With sound: …" or "Silent: …".
- **For a KIFF account:**
  - **"Alerts on this Windows PC"** is on only when the person turned it on for this account and Windows allows tafs's
    notifications.
  - **Turning it on** checks Windows first, as the iPad asks the system. Refused, it says "Allow notifications for tafs in
    Settings, then try again." with a link to Windows' notifications settings, and stays off.
  - **The choice is kept for each account** (off until turned on, as the iPad's `alertsEnabledKey`), so an account never
    inherits another's.
  - Both choices are kept beside the session, in `alerts.json`.
  - **Sound** ("For a hold or a halt only") is the PC's, on until turned off. It's off and greyed while alerts are off,
    and keeps its choice for when they're back. With Sound off, it goes to Notification Center without a banner or sound,
    as the iPad's passive alert (`PushDelegate.swift:81-82`).
- **Not here, as on macOS:**
  - the footer, which says this device tells you "even with tafs closed": on Windows an alert comes only while tafs
    runs;
  - "What alerts you", which tafs cloud keeps per phone's push token, and this PC has none;
  - Haptics, the phone's.

**Not yet:**
- **The KIFF account's rows**, from `GET /v1/me`:
  - KIFF account (its name, opening its details);
  - Signed in as (the email), as its own row, shown only when the account isn't named after it;
  - the plan, from `plan_line` in `plan_tone`'s colour.
- **View only from `GET /v1/me`** itself: today it comes from the token's `approval_mode`, kept with the session.
- **The KIFF-only sections:** People & apps, Coach's suggestions, the AI-data consent, the time zone and Your chats.
