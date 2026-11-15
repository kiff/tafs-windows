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

## People & groups (`People`, `PeoplePage`)

Ported from the iPad's `PeopleGroupsView` and `refreshPeople`, first in **People & apps**. tafs-macos #67 is a second
reference, read-only as here.
- **The read:** `GET /v1/groups`, `GET /v1/tenant/members` and `GET /v1/me` as the session, when it opens and every 20
  seconds while open. A group needs its id, name, members and agents; a person their subject, role and status. A field
  of the wrong kind fails the read, which says "People and groups couldn't be read." with why, over what was shown.
- **The account line:** "Acme · 2 people", counting only the people on the account now, or the count alone when the
  account is named after the person's email.
- **Each group:** KIFF's default group first, as "Everyone" in the app's language, then by name. Its name, 15 semibold,
  over what it uses, 12 dim: "Uses Invoices and Refunds", only the agents that are the account's tafs, or "Uses no taf
  yet".
- **Each person:** managers first, then by email. Their initial on a raised circle, their email (else KIFF's id for
  them), and "Manager" in amber or "Member".
- "No groups yet." once read, with no groups and no error.
- **Not yet:** the iPad's changes (a new group, renaming or deleting one, adding someone, making them the manager or
  removing them). Each is an approval signed on the device (`changePeople`), as Devices' Sign out.

## Export my chats (`ChatsExport`, `ShareFiles`)

Ported from the iPad's `ExportChatsSection` (ExportChatsView.swift), `AppModel.exportChats` and `TafsCloud.exportChats`
/ `splitExport`, for a KIFF account, after Language. tafs-macos #79 is a second reference.
- **The read:** `GET /v1/me/chats/export` from tafs cloud, as the session. `readable` is the text file; the rest is the
  JSON file, indented, its keys sorted at every level, slashes and letters left as they are. An answer that isn't an
  object, or a read that fails, says "Your chats weren't exported. Check your connection and try again."
- **The files:** `tafs-chats.json` and `tafs-chats.txt`, in tafs's folder under Windows' temporary folder. It's removed
  and made again at each export, and removed at every session change (sign-in, pairing, sign-out), so a person's chats
  don't stay on this PC after them. Files Windows won't write say "this Windows PC couldn't save the files."
- **The row:** "Export my chats", with a spinner and "Preparing your chats…" while it's made, and "Try again" after a
  failure, with why in red. Once made, Windows' share sheet opens with both files, and "Ready: tafs-chats.json,
  tafs-chats.txt. Share again" opens it again. The footer says what's in it and that only they can export it.
- **Differences:**
  - The iPad's share and save sheet is Windows' share sheet, which shares to apps and nearby devices.
  - The JSON's layout is .NET's indented one (`"key": value`), where Apple writes `"key" : value`; the content and key
    order are the same.
  - The iPad writes with iOS's file protection, which Windows has no counterpart for in the temporary folder.

## Devices (`Devices`, `DevicesPage`)

Ported from the iPad's `DevicesView` and `KIFFClient.Device`, opened from **People & apps**, which shows for a KIFF account
(the iPad's `SettingsView`):
- **The read:** `GET /v1/me/devices` as the session, as the iPad's `refreshDevices`. Each device needs its id. Its name is
  KIFF's `display_name` in the app's language, else the name it gave at sign-in, else "A device". Last used is
  `last_used_at`, else `last_seen_at` for a session from before devices. A field of the wrong kind fails the read, which
  says "Couldn't load your devices." with why, in the iPad's plain words (`KiffErrors`, its `KIFFError` and `PlainError`):
  signed out of KIFF for a 401, else the app's line for the status, never KIFF's own reason.
- **Each row:** the name, 15 semibold, and "Last used 3 hours ago" or "Not used yet", 13 dim. On the right, "This device"
  for this PC.
- **Last used** is written as Apple's `RelativeDateTimeFormatter` writes a past time in full, in each shipped language
  ("3 hours ago", "il y a 3 heures", "3 godziny temu"). .NET has no such formatter (`RelativeTime`). The unit is the
  largest with at least one, counted from the time elapsed: a month is 30 days and a year 365, where Apple counts
  calendar units.
- **The footer:** what signing a device out does, and that this PC signs out with Sign out of KIFF.
- **Sign out, for another device,** is an approval, signed by this device (`prepareRemoveDevice` behind Face ID on the
  iPad). Until this PC can sign one, it's left out, and Android's line says why under the list: "To sign another device
  out, use a device that can approve."
- **Not for Windows:** the iPad's **Add a device** (`PairComputerView`), where a phone adds a computer by its code. On
  Windows, this PC is the computer, and it's added from the phone.

**Not yet:**
- **Signing another device out,** with the approvals.
- **The KIFF account's rows**, from `GET /v1/me`:
  - KIFF account (its name, opening its details);
  - Signed in as (the email), as its own row, shown only when the account isn't named after it;
  - the plan, from `plan_line` in `plan_tone`'s colour.
- **View only from `GET /v1/me`** itself: today it comes from the token's `approval_mode`, kept with the session.
- **The KIFF-only sections:** the rest of People & apps (Connect apps), Coach's suggestions, the AI-data consent, the time zone and Your chats.
