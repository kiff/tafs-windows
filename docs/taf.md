# A taf's page

A taf's page is the iPad's `TafDetailView`, as in the reference set's `taf.png`, `taf-card.png` and `taf-setup.png`. It opens on the
Activity. The core is `Tafs.Core.Taf` (`TafScreen`, `TafActivity` and `TafSetup`), and the window shows it as `TafPage`.

- **The head:** the avatar (34, corners of 10, the name's first letter in white on the taf's colour), the name in 20
  bold, and its state's chip ("Working"), green on the same green at 15%.
- **The tabs:** Chat, Activity, Card and Setup, in the iPad's order, on the system grey track (`taf.section.*`, named
  "View"). Activity, the Card and Setup are built and open on every taf, as the iPad's: a real taf's Cards as Home
  read them from KIFF (`TafCards.Shown`: with none listed, one merged from its rules, as `Taf.shownCards`), its Setup
  read from tafs cloud; each sample taf with its own Card (its template's rules, as the iPad's `TafTemplates`) and job.
  A real taf whose Card KIFF revoked (`card.active` false) opens revoked, as the iPad's `card.revoked`: every Card
  says Revoked and fades, and the chip says Stopped. Chat opens on a sample taf and on one tafs cloud runs, as the
  iPad's `canChat`, and a taf's page opens on it, as the iPad's, unless it's opened on another tab.
  The Card bar is above the Chat and the Activity, and its chevron opens the Card tab.
- **The Chat** (`TafChat`, the iPad's `TafChatView`): the taf's thread, read from tafs cloud each time it shows
  (`GET /v1/tafs/{id}/conversation`, Coach's thread's shape, so `CoachThread.Read` reads it), in a column 720 wide.
  Each message once, by time. A time between parts an hour or more apart (`CoachChat.GapTime`). The person's words on
  the right in the brand bubble, with "Answered the card above" under an answer; the taf's full width at 16, with
  "Worked for 12 s ›" above them, which opens its steps in the taf's words (`TafChat.ActivityWords`, the iPad's
  `tafActivityWords`: "Reading its notes…", "Asking KIFF to run stripe refund…"); where its result went above its words
  and what the run did under them, tafs cloud's lines in 13 soft; a refused connection's lines, the first in red. A
  run's start is a centred line, "Run started 08:00 · …"; tafs cloud's lines are only ever around the taf's own words
  (`ChatMessage.RunLines`, `RefusedLines`, the iPad's `runLines`, `refusedLines`). A message through one of the taf's
  doors keeps its stamp (`ChatDoor`, the iPad's `DoorMark` and `DoorStamp`, mono 10, blue, red when not sent back): "BY
  EMAIL · who" over it, on the right, and under the taf's answer how it went back ("↩ SENT BACK BY EMAIL · SAME
  THREAD", "NOT SENT BACK · why", "GOES BACK BY EMAIL WHEN DONE"). The files sent with a message are drawn above its
  words, each its name with its kind's mark, as `MessageFiles`; a message with only files keeps them. With nothing in it, `chat.empty_named`; a read that doesn't
  come keeps the thread there was and says "The thread couldn't be loaded." and why, in red at 13. A sample taf's
  thread is its greeting (`sample.hello`), with what it's doing as its Overview line says it. Coach's thread draws
  its bubbles, run starts and work the same way (`ChatViews`).
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

**Its work and its runs** (S17, S18; `TafWork`, `TafHealth`, `TafPage.Work.cs`; the iPad's `WorkPanel`, `HealthLines`
and `RunsPanel`): above a tafs cloud taf's record on Activity, from `GET /v1/tafs/{id}/work`, read as the page opens and
after each change.
- **Its work:** tafs cloud's line (unless its health's first reason says it); its health's lines in amber, red when
  failing, each with tafs cloud's fixes (Coach with its words in the box, nothing sent, for whoever manages it and may use
  Coach; Plan & usage for credits); its oldest waiting case, amber past the promise; Answer within (`PUT …/promise`,
  whole hours or Not set) for whoever manages it; then its requests and skipped firings, newest first: needing you amber,
  failed red, skipped dim, what a run used, where its result went, and Send again on one given up on
  (`POST …/requests/{run}/deliveries/{place}/send`, the place's key URL-encoded). A read that fails says so in red.
- **Its runs:** that it works from its chat, or when it runs next (Paused when it is), and Run now for whoever manages it
  (`POST …/run`), "Starting a run…" until tafs cloud answers; a firing that started nothing says tafs cloud's words.
- The runs it kept, and a run's own page, come with S19.

**Starts when and Results go to** (P40, `TafKnob`, `ResultPlaces`, `TafPage.Starts.cs`; the iPad's `StartsWhenView`,
`ResultsGoToView` and `KnobPendingPanel`): two rows under the job, each its own screen with Setup to go back, read from
the taf's dial (`GET /v1/tafs/{id}/knob`, read as strictly as the iPad's decoder) each time one opens and after each change.
- **Starts when:** its schedule (each line, its size, "Next run Fri 12:30 · UTC" unless paused, and If busy as set), or
  "No schedule starts it." ("When you tell it, in its chat." for one that works from its chat); the app events it listens
  to, "Listening" in green once approved; the follow-ups it set itself, each with Remove (`DELETE …/follow-ups/{id}`);
  Change when it starts with Coach; then Pause every start or Resume every start (`POST …/pause`, `…/resume`), not for a
  taf that works from its chat.
- **Results go to:** "At least one place stays on."; tafs' own places (reply to the sender, alert our phones, email me),
  switched at once (`PUT …/results`) from the live taf's (Home's `results`, then each answer's), the last one on never
  off; then the places in the person's connected apps as the dial has them, and Add a place from a connected app with Coach.
- **A change waiting as the next version:** "DRAFT · V4 · NOT LIVE" and its words, for whoever manages the taf.
- **Not here yet:** what makes a next version (a schedule's − and +, If busy, an app event's Approve or Stop, an app's
  place) and Make it live come with the versions' own change; the doors with the Doors tab (P13); Not sent with the taf's
  work (S17).
- Only whoever manages the taf changes anything; a change goes one at a time, and why one didn't go through is said in red.
- `--page taf-starts` or `taf-results` with `--home` opens them as whoever manages the first taf, its dial from
  `taf-knob.json` beside the Home file (`tests/fixtures/taf-knob`; CI's `taf-starts` and `taf-results`).

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
    for whoever manages the taf, as KIFF says (`GET /v1/me/access`, the iPad's `manages`, read again with each read of
    Home's list, as its `refreshAccess`, and kept as it was when it can't be read), and nothing shows until that's read.
    It never shows for a revoked taf, as the iPad's `taf.state != .revoked`: tafs cloud's `stopped`, or any state it
    doesn't know (`Halt.HasRow`), nor once it's revoked on its page. A tafs cloud taf's Card tab opens for that row even before its Cards
    are drawn here.
  - **Every taf's halt** is a row in the sidebar, under the screens, as the iPad's `haltAllRow`. It shows to someone who
    manages the account's Cards (`cards.manage`, or when KIFF doesn't say, from `GET /v1/me`, read with each read of Home's
    list, every taf's halt then read too, whatever order KIFF's reads land in), while a taf runs, and
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

**Writing to the taf** (`TafChatSend`, ported from the iPad's `sendChat`, `SentMessages` and `stopChat`):
- **The composer** sits under the thread for a taf tafs cloud runs: "Message Refunds", or "Send to Refunds while it
  works" while a run of its is at work. Enter sends; Shift+Enter is a new line.
- **Sending:** an empty message sends nothing and says nothing; one over 2000 characters says "That's longer than 2000
  characters…" and sends nothing. Otherwise it shows at once, never above the thread's latest, and goes to tafs cloud
  (`POST /v1/tafs/{id}/messages`, `{text}`). One at a time: Send is off while it goes.
- **Until the thread has it,** it stays on screen after the thread, matched once by its id, else its words, for two
  minutes at most.
- **Not taken:** it stays in the thread where it was written, as the iPad's `UnsentBubble` (`Unsent`): the bubble faded,
  "Not sent · Too busy." in red under it (tafs cloud's words for why, else not reached), then **Edit**, which puts its
  words back in the box and takes it out of the thread, and **Retry**, which sends it again once. Its menu has Copy and
  Delete. Not tried at all (another message going), Retry leaves it where it was.
  - **Refused for a halt** (tafs cloud's `halted`: the `account`, or the `taf` on its own), as the iPad's: a raised hand in
    place of the error mark, and **Resume** before Retry, which can't pass a halt. Resume opens Halt's screen, the
    account's or this taf's, where resuming is the person's act behind Windows Hello; nothing resumes by itself. Retry
    sends it once that's done.
  - **Refused for want of the AI-data consent** (`ai_consent_required`), as the iPad's: no Not sent row. The words go
    back in the box, and the consent's screen says why (`docs/account.md`); sent again once it's allowed.
  - **Retry is a new send.** tafs cloud doesn't take an idempotency key for a message yet (kiff-admin's, in tafs cloud), so
    a send that reached it but whose answer was lost can show twice after Retry.
- **While the taf works,** or a sent message waits, the thread is read again every 2 s, only while Chat shows.
- **Stop,** for whoever manages the taf (KIFF's `cards.manage` access, once read): the run at work is cancelled
  (`POST /v1/tafs/{id}/stop`, `{session}`), then "You stopped Refunds.", or "Not stopped." and why.
- A sample taf has no composer yet; the iPad's answers its own words in sample mode.
- **The answer as it comes** (`TafChatStream`, `ServerSentEvents`, `LiveAnswer`): a message goes with its answer streamed
  (`POST /v1/tafs/{id}/messages/stream`). Under it, the live line with a spinner: tafs cloud's `state` ("Thinking…", the
  step's words or Coach's note with "…", "Waiting for you"), and before that the step at work, else "Thinking…". The
  steps follow, a failed one in red with tafs cloud's why, then the words as they come. The model's thinking text is
  never drawn. At the end the thread is read again and its stored answer takes the live one's place; a stop with words
  (an `ai_unavailable` stop, say) says them, whole. A stream that doesn't open (not reached) sends the message plainly; a
  refusal is said and never sent again; a stream that breaks leaves the rest to the thread. Narrator reads the live line
  politely as it changes.

**Resume** (`HaltResume`, ported from the iPad's `resumeHalt`), on a taf's Halt screen or every taf's:
- **Who:** only a passkey session that knows its passkey, the same rule as Approve once (`HeldAnswers.Approves`). Any other
  session reads "This device can't approve. Approve on another device signed in to your account." in its place.
- **One Windows Hello:** "Resume with Windows Hello". This PC's passkey asserts KIFF's resume itself
  (`KIFF-Passkey-Assertion` over `kiff.request.v1`, `POST /v1/agents/{agent}/resume`, or `/v1/resume` for every taf),
  since resuming gives authority back and KIFF takes it only signed by the approval key.
- **Then tafs cloud** releases its halt (`DELETE` on the halt), which lets the taf run only once KIFF says it's resumed.
  Taken, the screen reads Running, and the list and the sidebar are read again.
- **Not taken:** closing Windows Hello says "Cancelled, so nothing was approved…"; any other failure to sign, "Not resumed:
  it wasn't confirmed."; KIFF not taking it, "KIFF didn't resume Rae." with the app's line for its status; tafs cloud not
  taking it after KIFF did, "KIFF resumed Rae, but tafs cloud didn't yet." with why. A sign-out meanwhile sends nothing
  more. A taf halted with every taf is resumed from every taf's screen.

**Remove for good** (`TafRemoval`, ported from the iPad's `removeTaf` and `removalWords`): "Remove Rae", outlined in red,
last on the Card tab, for whoever manages the taf as KIFF says, on a PC that signs with its passkey, as Resume (a removal
is the approval key's act, never the device key's); a revoked taf can still be removed.
1. **The confirmation:** "Remove Rae for good?", what always goes (`taf.remove_body`), then what goes with it and what
   stays, in tafs cloud's words (`GET /v1/tafs/{id}/removal`, which ends nothing). Cancel changes nothing.
2. **Signed first,** behind one Windows Hello: this PC's passkey asserts `kiff.request.v1` over `DELETE /v1/tafs/{id}`
   and its empty body, sent as `KIFF-Passkey-Assertion` (read by tafs cloud from kiff/tafs#1247 on). Closed or refused:
   "Not removed. Nothing changed.", and nothing is sent.
3. **Its Cards revoked in KIFF** (Home's `card.ids`, `POST /v1/me/mandates/{id}/revoke`), unless it's revoked already;
   one KIFF doesn't revoke is counted and said.
4. **tafs cloud removes it** (`DELETE /v1/tafs/{id}`): done, what went ("Its Cards are revoked in KIFF. Its access to apps
   is removed. Its schedule, chats, run reports, memory and key are deleted, and the name Rae is free."), then Home is
   read again and the Overview opens; not finished, which steps are left, tried again on each refresh; not answered,
   why, and that it finishes on the next refresh if it began. A sign-out at any point ends it quietly.

**Not yet:**
- **The revision as If-None-Match:** the server's ETag is the quoted form of `revision`; each read is whole for now. Its
  days are titled as sent, in 12 semibold, not in the sample's capitals. Pull to refresh has no Windows gesture yet.
- **Chat's Retry** on a message kept unsent, and steering a run at work (`/steer`).
- **Chat's other items:** held calls in the thread and the Needs you pill, what became of an approved call, a door's
  knocks, a setting handed to Coach, a run stopped at its cap, the answer's Report menu, opening a sent file full size
  (it's named only, until its bytes are read), and a connected tool's title
  in its steps ("Asking KIFF: Create refund…", from planque's grants). The taf's words are drawn as written, not as
  Markdown, as Coach's are.
- **What acts in Setup:** a connected app's sheet, the + to connect one, a Card-unit exception's Coach and Dismiss
  (signed), the taf's own Connect again, Improve in Coach, Change the workflow with Coach, the memory's See all, Starts
  when and Results go to. They need Coach, signing or sign-in, and whether the person manages the taf. The
  sample's Forget and Pause change its page only.
- **The real Revoke:** `POST /v1/me/mandates/{id}/revoke` for each of its Cards, shown only to whoever KIFF says manages
  the taf, and Stopped only once KIFF has revoked them all (`card.revoke_failed` otherwise). How it's signed, and
  whether a view-only session may send it, is settled in its own PR with the security review. Today's Revoke reaches
  nothing, so it's shown on the sample's Card tab only, never on a real taf's.
- A Card's pending use and unit words drawn, its totals for each item, and how KIFF counts
  each tool.
- **The rest:** the head's second line (the version and Share); held calls, Card-unit exceptions, its work and its runs
  at the top of the Activity; a line's receipt and its next step.
