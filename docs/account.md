# Account

Account is the iPad's `SettingsView`, as in the reference set's `account.png`. The core is
`Tafs.Core.Account.AccountScreen`; `AccountPage` builds it from rows in the grouped list's style (see
[connect apps](connect-apps.md)).

The sections, in the iPad's order:
- **Your account:** Mode, as Sample data, Signed in with KIFF, or Signed out.
  - **The KIFF account**, once `GET /v1/me` is read (`KiffAccount`, as the iPad's `KIFFClient.Account` and `AccountView`):
    KIFF account (its name, else its slug), Account id (only when it differs from the name), Signed in as (the email,
    left out when the account is named after it, as `AccountLines.signedInAs`), and Time zone (KIFF's, else UTC).
  - **Use this Windows PC: Europe/Paris,** under Time zone when the account's differs from this PC's (Windows' zone as its
    IANA name), for a session KIFF lets change it (`account.settings`, or a KIFF that lists none) and never a view-only
    one (`KiffAccount.Offered`), as the iPad's "Use this iPad": Windows Hello asks first ("Set your account's time zone to Europe/Paris"),
    then this session's TPM key signs KIFF's `PUT /v1/me/time-zone` as a signed setup call (`KIFF-Device-Signature` over
    `kiff.request.v1`). Closed or failing, nothing is sent ("The time zone didn't change."); KIFF refusing says the app's
    line.
  - **Then tafs cloud moves the schedules,** as the iPad's `syncTimeZone`: once KIFF has the zone, `POST /v1/account/time-zone`
    (no body) moves every recurring schedule of the account's tafs to it. If it can't yet, the zone is still changed and
    "The account's zone changed. Its schedules couldn't be updated yet:" says why. Then the account and Home are read again.
  - **Any zone** (the iPad's `TimeZonePicker`): for the same sessions, Time zone opens its list, "Use this Windows PC: …" first,
    then tafs cloud's zones (`GET /v1/time-zones?q=`), searched there a moment after the last key ("Find a time zone"),
    the account's marked with a check. One chosen is set as this PC's is, behind the same Windows Hello and signature. A
    list that can't be read says so in red ("Time zone couldn't be read:" and why).
  - **Delete account…,** in red, for someone KIFF lets delete it (`account.delete`, or a KIFF that lists none) on a session
    that isn't view-only, as the
    iPad's `DeleteAccountView` (App Store guideline 5.1.1(v)): its warning, what goes (tafs, runs, chats and notes; Cards
    and keys; app sign-ins; KIFF records after 30 days), and "You confirm with Windows Hello. Other devices sign out too."
    Then, in the iPad's order (`AccountDeletion`): Windows Hello ("Delete your account"); tafs cloud erases every taf
    (`POST /v1/account/delete`, `{done}`); planque forgets the connections (`DELETE /v1/accounts/{tenant}`, signed by this
    device); KIFF, last (`POST /v1/me/delete`, signed by the TPM key as a setup call). Each step that fails says which part
    is left (only the owner can, not erased yet, connections left, KIFF didn't delete), and trying again goes on from
    there. Deleted, this PC forgets the session as a sign-out does and opens sign-in.
  - **In the sidebar,** under Account: the person's email, else the account's name, once read. Narrator reads both.
  - **View only**, with Add this Windows PC, shows exactly when the kept session's `approval_mode` says view ([the session](session.md)).
- **People & apps,** for a KIFF account: People & groups, Devices, and Connect apps, which opens the sidebar's screen.
- **Plan:** Plan & usage, which opens its screen.
- **Coach,** once tafs cloud's setting is read (`Suggestions`, `GET /v1/me/suggestions`): Show suggestions, a switch with
  tafs cloud's words for what it does now. Only someone who manages the account's tafs (`cards.manage`) can change it
  (`PUT {show}`); refused, why shows in red.
- **Your data and the AI,** after Theme, once read (`AiConsent`, `GET /v1/me/ai-consent`), every word tafs cloud's: given,
  its line and Withdraw in red (`DELETE`); not given, its title, which opens its screen (its lines, Allow for the version
  read with `POST {version}`, and Not now). A change that isn't taken says why in red.
- **Alerts:** a row that opens. The alerts themselves aren't built yet.
- **Theme:** Light, Dark or System, the iPad's segmented control. It sets the window's theme, and it's kept for the
  next launch (`ThemeChoices`), as the iPad keeps `tafs.theme`.
  - **Where:** one word (`light`, `dark` or `system`) in `%LOCALAPPDATA%\tafs\theme`, beside the session. It's written
    beside the file and moved over it, so it's whole or not there.
  - **What can't be read** is the system's. A theme that can't be kept still shows now.
  - **At launch,** `--theme light|dark` wins, for CI's screenshots. A screenshot never reads what this PC kept, so the
    same command gives the same picture anywhere.
- **Language:** the app's language, by its own name ("Français"), and Change language, which opens the app's own picker,
  as Android's (tafs#1214):
  - **"Language"**, with "This Windows PC's language" first (`settings.language_device`), then English, Deutsch, Français,
    Español, Italiano, Português, Nederlands and Polski, each named in itself (`AppLanguage.Named`), the current one marked.
  - **Choosing one** keeps it in `%LOCALAPPDATA%\tafs\language` (a bare code; this PC's language removes it) and starts tafs
    again at once in it (`AppInstance.Restart`, else a new process), still signed in, since the session is kept. The one in
    use changes nothing. A choice that can't be kept says so and changes nothing.
  - **At launch,** `--lang` wins (screenshots), then the kept choice, then Windows' languages (`AppLanguage.Resolve`). A
    kept code tafs doesn't ship is ignored. tafs cloud and KIFF follow at once, through `Accept-Language`.
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
