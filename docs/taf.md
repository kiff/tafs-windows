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
- **The stack:** its Cards as in a wallet. The front one is whole, and each one behind shows a 28 band of its top edge,
  a shade darker the further back, with the furthest on top. A tap brings one to the front, and Narrator reads the
  front one figure first.
- **A Card's face:** 300 wide, a payment card's shape, corners of 18, in white on the taf's colour (a test Card in
  grey), with the iPad's ring at its corner. It shows its name and environment pill, its figure, its facts, who issued
  it, and "No end date" when it has none.
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

**Not yet:**
- **Reading it from tafs cloud:** the next pages, Try again, and the revision as If-None-Match. The server's ETag is
  the quoted form of `revision`. Its days are titled as sent, in 12 semibold, not in the sample's capitals.
- **Opening a taf from the sidebar's Your tafs and the Overview,** once they list the tafs.
- **Chat,** in its own change.
- **Setup from tafs cloud:** drawing what `TafSetup.Read` reads. That means the connected apps and their sheets, the
  Card-unit exceptions (whose change Coach asks to sign), Connect again, the memory's See all, Improve in Coach, and
  Remove. Forget and Pause also need to reach tafs cloud; for now they change the sample's page only.
- **Revoking at KIFF,** signed; a Card's pending use and unit words drawn, its totals for each item, and how KIFF counts
  each tool.
- **The rest:** the head's second line (the version and Share); held calls, Card-unit exceptions, its work and its runs
  at the top of the Activity; a line's receipt and its next step.
