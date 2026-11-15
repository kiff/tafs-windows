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
    tafs, the pages' reads and Setup's proof, this device's id, and Coach's thread, answers and sending. A read the
    session started that lands afterwards is dropped. The window then opens sign-in. If Windows refuses to forget the session, it
    says "Sign out didn't go through" with Windows' error code only, and stays here to try again.
  - signed out: Sign in with KIFF.

**Not yet:**
- **The KIFF account's rows**, from `GET /v1/me`:
  - KIFF account (its name, opening its details);
  - Signed in as (the email), as its own row, shown only when the account isn't named after it;
  - the plan, from `plan_line` in `plan_tone`'s colour.
- **View only from `GET /v1/me`** itself: today it comes from the token's `approval_mode`, kept with the session.
- **The KIFF-only sections:** People & apps, Coach's suggestions, the AI-data consent, the time zone and Your chats.
