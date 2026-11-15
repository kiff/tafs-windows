# A taf's page

A taf's page is the iPad's `TafDetailView`, as in the reference set's `taf.png`, `taf-card.png` and `taf-setup.png`. It opens on the
Activity. The core is `Tafs.Core.Taf` (`TafScreen`, `TafActivity` and `TafSetup`), and the window shows it as `TafPage`.

- **The head:** the avatar (34, corners of 10, the name's first letter in white on the taf's colour), the name in 20
  bold, and its state's chip ("Working"), green on the same green at 15%.
- **The tabs:** Chat, Activity, Card and Setup, in the iPad's order, on the system grey track (`taf.section.*`, named
  "View"). Activity, the Card and Setup are built. Chat is shown, so the page has the iPad's shape, and is off.
  The Card bar is above the Activity, and its chevron opens the Card tab.
- **The Card bar:** a small Card in the taf's colour with the Card's mark, its Card's figure and words ("€1,960 left
  today of €2,000"), and under it the Card's name, its environment when it isn't production, and "· 3 Cards" when the
  taf holds more than one (`TafCard.Bar`).
- **The Activity:** the filters as chips, the chosen one inverted (Done is the iPad's own word on a taf's page,
  `taf.halt.part_held`, not the Record's), then the days, "TODAY · NOV 15". Each line is drawn as the Record draws it:
  its dot (green done, approved or started; amber held, sending or unknown; red refused, not done or revoked), what it
  was, the words under it, its time, and `amount_text` when tafs cloud sends it. Under All with nothing to show it says
  "Nothing yet.", once narrowed "Nothing here.", and tafs cloud's `end_words` go under the last line.

**tafs cloud's Activity:** `TafActivity.Read` reads `GET /v1/tafs/{id}/activity` (`contracts/taf-activity.json`).
- It reads the days, the lines, the filters with the one applied, the end words and the revision, in tafs cloud's words
  and order.
- A kind this app doesn't know reads as Unknown, so it waits under amber rather than passing as done. An entry with no
  id or title is left out.
- The filters are tafs cloud's. The app sends the chosen one back.

**A taf from tafs cloud** (opened from the Overview or Your tafs) reads its Activity there, page after page
(`ActivityPages`, ported from `SourcePages` and `AppModel.loadActivityPage`, and drawn as `TafPresentationViews.swift`'s
Activity):
- **The read:** `GET /v1/tafs/{id}/activity?filter=…`, with tafs cloud's selection when it gives one and `&cursor=…` for
  the next page, escaped as the iPad's `escaped` and `query`. It goes through the same read as Home's, so the session's
  token goes only to the exact hosts.
- **When it reads:** on opening, and each time a filter is chosen (that filter's newest page); the next page when its
  ring comes into view, as the iPad's `.task` on its ProgressView. One asked for while a read is going waits for it.
- **What it draws:** the first page's filters, every page's days (a day going on from the page before has no title),
  then what ends them, in the iPad's order: why a read failed, in red, with Try again (the next page when some are in);
  the next page's ring; a read going; tafs cloud's end words; or, with no line at all, "Nothing here.".
- **A more read refused with 409** (its history moved on) starts again from the newest page; any other failure keeps the
  pages and says why, as `CloudRead` words it.
- `--home shared/contracts/home.json --page taf` opens the first taf, its Activity from `taf-activity.json` beside it.

**Its Setup from tafs cloud** (`SetupRead`, ported from `AppModel.loadTafSetup`; drawn as `TafDetailView.setup`,
`ConnectedToolsRing` and `MemorySummaryPanel`): `GET /v1/tafs/{id}/setup`, read as the page opens and on Try again.
- **With planque's proof,** as the iPad: its signed grants read (`Planque-Device-Signature`), signed with the session's
  TPM key, so tafs cloud can read its connected apps ([keys](keys.md#planques-proof-for-a-tafs-setup)). When it can't be
  signed, Setup reads without it, as the iPad's `try?`, and tafs cloud says so where the connections would be ("Connect
  this phone to read its connected apps", its word "phone"): no app on the ring, and no Connect again.
- **In the iPad's order:** the Card-unit exceptions (title, words, history); the job (Home's `job_summary`, else "Works
  under 2 KIFF Cards."); the connected apps; what it remembers ("3 notes" and the latest, two lines at most).
- **The connected apps:** why they couldn't be read, in red, with Try again; a ring while it's read; then the ring, 280
  high: each app on a circle round the taf, joined by a dashed line, its name and its actions' words, a running one with a
  green arc and green words. One node an app (`TafSetup.Nodes`): the connection with actions in front, else one to sign in
  again, else the first. The taf in the middle shows its job. Under it, the workflow folded: its title and summary, then
  each step's number (green while it runs), label, words and app.
- A failed read keeps the page read before, as the iPad keeps `tafSetups` beside `tafSetupErrors`.
- `--page taf-setup` with `--home` opens the first taf on Setup, from `taf-setup.json` beside the Home contract (CI's
  `taf-home` and `taf-setup-home`).

**Setup**, as `taf-setup.png`:
- **The job,** under "THE JOB".
- **Starts when and Results go to:** shown, off until their screens are built.
- **What it remembers:**
  - each note with its line under it, its file's path as words and its time ("customers · regulars · 02:57");
  - Forget, which asks first, as the iPad does ("Forget this note?"), since it can't be undone;
  - the footnote on what notes do.
- **Pause and Resume,** the state chip turning from "Working" to the soft "Paused".

**tafs cloud's Setup:** `TafSetup.Read` reads `GET /v1/tafs/{id}/setup` (`contracts/taf-setup.json`,
`taf-setup-connected.json` and `taf-setup-reconnect.json`), as the iPad's `SetupView`.
- It reads the connections, with their status words, actions and limits, and their week with its Activity query. When
  the connections couldn't be read, it reads why (`connections_error`).
- It also reads the workflow and its steps, the memory and versions in short, the Card-unit exceptions waiting, and the
  taf's own Connect again.
- What can't be read is left out, never the page with it. A Connect again without its connection, title or words reads
  as none, as the iPad's `usable`.

**The sample:**
- `--page taf --sample` opens Refunds, as the reference set does; `--page taf-setup --sample` opens it on Setup.
- Its Activity is Refunds' own lines of the sample Record: the credit on #2290, the refund of #4471, and its start.
- The iPad's filters narrow them: Done keeps done and approved; Held, held; Refused, refused, not done and revoked.
- Its Card is €2,000 a day, €40 of it used today.

**The Card tab**, as `taf-card.png`:
- **The stack:** its Cards as in a wallet. The front one is whole, and each one behind shows a 28 band of its top edge
  (the iPad's is 26; 2 more keeps a band's name whole in Segoe),
  a shade darker the further back, with the furthest on top. A tap brings one to the front, and Narrator reads the
  front one figure first.
- **A Card's face:** 300 wide, a payment card's shape, corners of 18, in white on the taf's colour (a test Card in
  grey), with the iPad's ring at its corner. It shows its name and environment pill, its figure with its words in 12
  medium at 0.9, its facts, who issued it, and "No end date" when it has none. A long fact shrinks to 80% at most, as
  the iPad's `minimumScaleFactor(0.8)`, then ends in "…". Each Card casts the iPad's shadow up onto the one behind it
  (black at 16%, radius 4, 2 up), drawn as a soft band over its top edge.
- **Under the stack:** every rule of the front Card, its name and what happens outside it on the left, and its limits
  on the right in mono ("never" in red).
- **Revoke,** in red, asks first ("Revoke Refunds' Card?", Revoke and stop). In the sample, the chip turns to a red
  "Stopped", the Cards fade, and Revoke and Pause go.

**Cards** (`TafCard`, the iPad's `TafCard` and `CardRule`): a taf holds one KIFF Card or several, each with its own
rules and totals.
- **Amounts are KIFF's:** an integer with its unit and scale (`KiffAmount`), shown in the app's language. A currency
  keeps its own symbol as that language writes it ("€12.50"; the code itself where the language has no region using
  it), another unit follows the number ("1,500 credits"), and a bare number says so ("30,000 (no currency)").
- **Extra room stays apart:** tafs cloud's limit and a rule's `per_window` include it. With `extra_room` and
  `base_limit`, the Card's own limit and the room show apart: "left today of €2,000 + €30 extra room until 00:00",
  "€2,000 in total a day, then it asks you · +€30 extra room today", never "of €2,030". With room but no `base_limit`,
  no total shows at all: neither the figure nor the rule's total, since KIFF's has the room folded in.
- **Its figure** (`Hero`) is what's left of its money total, else of its calls. What's left shows only when the use is
  known in the limit's own unit and scale. Otherwise the limit shows alone, "€2,000 a day · use unavailable". No figure is
  worked out here beyond KIFF's limit less KIFF's use.
- **A rule's limits** read as KIFF's Cards say them: "up to €150 each; more asks you first · €2,000 in total a day,
  then it asks you" when past them it asks; "≤ €150 each · €2,000 a day" when it refuses; "never", "always asks you".
  Under its name: "Outside: held for you" or "Outside: refused", from KIFF's `outside` (`hold` or `refuse`).
- **What it carries for the Card tab:** a total's pending use and why it has no currency, in KIFF's words as sent.
- **Its facts:** its main rule and how many tools, or without a figure its first two rules and how many more.
- **A window** is KIFF's (`calendar_day`, `rolling_24h`, `rolling_7d`, `rolling_1h`, `lifetime`). tafs cloud's words
  come first, and one this app doesn't know reads as sent, never as a day.
- **The sample's Card** is Refunds' own: refunds up to €150 each and €2,000 a day, credits up to €100 and €1,000, never
  a payout, with €40 used today.

**Halt** (`Halt`, `HaltPage`), as the iPad's `HaltScreen`, `HaltRow` and `TafsCloud.Halt`, with tafs-macos #70 as a second
reference:
- **Who sees it:**
  - **A taf's halt** is a row on its Card tab, after its rules and before Revoke, as the iPad's `card(_:)`. It shows only
    for whoever manages the taf, as KIFF says (`GET /v1/me/access`, the iPad's `manages`), and nothing shows until that's
    read. It never shows once the taf is revoked. A tafs cloud taf's Card tab opens for that row even before its Cards
    are drawn here.
  - **Every taf's halt** is a row in the sidebar, under the screens, as the iPad's `haltAllRow`. It shows to someone who
    manages the account's Cards (`cards.manage`, or when KIFF doesn't say, from `GET /v1/me`), while a taf runs, and
    whenever the account is halted or halting, tafs or not (`showsAccountHalt`). Its second line, in red, is its state
    once it isn't running.
  - The access and capabilities are read with each session, and kept as they were when a read fails.
- **The screen:**
  - the taf's name, or "All your tafs", dim while running and red after;
  - Running, Halting… or Halted, and what halting does or since when it holds, in this PC's time;
  - once halting, who holds it, each part Done or Waiting: no run starts (kiff/agents), KIFF refuses its calls, the
    connection sends nothing (planque).
- **Halt now / Halt all now:** one click with no Windows Hello, since halting only takes authority away. `POST` to
  `/v1/tafs/{id}/halt`, or `/v1/halt` for every taf, as the session.
  - A halt tafs cloud answers reads Home's list and every taf's halt again at once.
  - It's read again every 2 seconds while halting, for up to 2 minutes.
  - Halt again re-asks a part that didn't take it.
  - A refusal says "tafs couldn't halt it." with why.
- **Resuming gives authority back,** so it's signed (KIFF first, behind Face ID on the iPad). Until this PC can sign it,
  where the iPad has Resume the screen says this device can't approve, and to approve on another device. A taf halted
  with every taf is resumed from every taf's screen, as the iPad's.
- **Decoded as the iPad's `Halt`:** its state one of the three and its four flags true or false, or the read fails;
  `halted_at` and `stopped` absent or null when not given. There's no halt fixture at the pin yet, so the shape is the
  iPad's reader.

**Not yet:**
- **Resuming** (`ResumeConfirmation`), with the approvals.
- **The revision as If-None-Match:** the server's ETag is the quoted form of `revision`; each read is whole for now. Its
  days are titled as sent, in 12 semibold, not in the sample's capitals. Pull to refresh has no Windows gesture yet.
- **Chat,** in its own change.
- **What acts in Setup:** a connected app's sheet, the + to connect one, a Card-unit exception's Coach and Dismiss
  (signed), the taf's own Connect again, Improve in Coach, Change the workflow with Coach, the memory's See all, Starts
  when, Results go to, and Remove. They need Coach, signing or sign-in, and whether the person manages the taf. The
  sample's Forget and Pause change its page only.
- **The real Revoke:** `POST /v1/me/mandates/{id}/revoke` for each of its Cards, shown only to whoever KIFF says manages
  the taf, and Stopped only once KIFF has revoked them all (`card.revoke_failed` otherwise). How it's signed, and
  whether a view-only session may send it, is settled in its own PR with the security review. Today's Revoke reaches
  nothing: the Card tab is only the sample's.
- A Card's pending use and unit words drawn, its totals for each item, and how KIFF counts
  each tool.
- **The rest:** the head's second line (the version and Share); held calls, Card-unit exceptions, its work and its runs
  at the top of the Activity; a line's receipt and its next step.
