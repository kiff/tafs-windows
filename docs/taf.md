# A taf's page

A taf's page is the iPad's `TafDetailView`, as in the reference set's `taf.png`. It opens on the Activity. The core is
`Tafs.Core.Taf` (`TafScreen` and `TafActivity`), and the window shows it as `TafPage`.

- **The head:** the avatar (34, corners of 10, the name's first letter in white on the taf's colour), the name in 20
  bold, and its state's chip ("Working"), green on the same green at 15%.
- **The tabs:** Chat, Activity, Card and Setup, in the iPad's order, on the system grey track (`taf.section.*`, named
  "View"). Only Activity is built. The other three are shown, so the page has the iPad's shape, and are off.
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

**The sample:**
- `--page taf --sample` opens Refunds, as the reference set does.
- Its Activity is Refunds' own lines of the sample Record: the credit on #2290, the refund of #4471, and its start.
- The iPad's filters narrow them: Done keeps done and approved; Held, held; Refused, refused, not done and revoked.
- Its Card is €2,000 a day, €40 of it used today.

**Cards** (`TafCard`, the iPad's `TafCard` and `CardRule`): a taf holds one KIFF Card or several, each with its own
rules and totals.
- **Amounts are KIFF's:** an integer with its unit and scale (`KiffAmount`), shown in the app's language. A currency
  keeps its own symbol ("€12.50"), another unit follows the number ("1,500 credits"), and a bare number says so
  ("30,000 (no currency)").
- **Its figure** (`Hero`) is what's left of its money total, else of its calls. What's left shows only when the use is
  known in the limit's own unit and scale. Otherwise the limit shows alone, "€2,000 a day · use unavailable", and no
  figure is ever worked out here.
- **A rule's limits** read as KIFF's Cards say them: "up to €150 each; more asks you first · €2,000 in total a day,
  then it asks you" when past them it asks; "≤ €150 each · €2,000 a day" when it refuses; "never", "always asks you".
  Under its name: "Outside: held for you" or "Outside: refused".
- **Its facts:** its main rule and how many tools, or without a figure its first two rules and how many more.
- **A window** is KIFF's (`calendar_day`, `rolling_24h`, `rolling_7d`, `rolling_1h`, `lifetime`). tafs cloud's words
  come first, and one this app doesn't know reads as sent, never as a day.
- **The sample's Card** is Refunds' own: refunds up to €150 each and €2,000 a day, credits up to €100 and €1,000, never
  a payout, with €40 used today.

**Not yet:**
- **Reading it from tafs cloud:** the next pages, Try again, and the revision as If-None-Match. The server's ETag is
  the quoted form of `revision`. Its days are titled as sent, in 12 semibold, not in the sample's capitals.
- **Opening a taf from the sidebar's Your tafs and the Overview,** once they list the tafs.
- **The other tabs,** each in its own change: Setup (from `taf-setup.json`, `taf-setup-connected.json` and
  `taf-setup-reconnect.json`), the Card, and Chat.
- **The Card tab:** the stack of Cards, every rule's lines, and Revoke. The Card bar's chevron opens it, so the chevron
  comes with it. Also a Card's extra room, its pending use, and how KIFF counts each tool.
- **The rest:** the head's second line (the version and Share); held calls, Card-unit exceptions, its work and its runs
  at the top of the Activity; a line's receipt and its next step.
