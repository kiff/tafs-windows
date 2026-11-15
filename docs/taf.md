# A taf's page

A taf's page is the iPad's `TafDetailView`, as in the reference set's `taf.png`. It opens on the Activity. The core is
`Tafs.Core.Taf` (`TafScreen` and `TafActivity`), and the window shows it as `TafPage`.

- **The head:** the avatar (34, corners of 10, the name's first letter in white on the taf's colour), the name in 20
  bold, and its state's chip ("Working"), green on the same green at 15%.
- **The tabs:** Chat, Activity, Card and Setup, in the iPad's order, on the system grey track (`taf.section.*`, named
  "View"). Only Activity is built. The other three are shown, so the page has the iPad's shape, and are off.
- **The Card bar:** a small Card in the taf's colour with the Card's mark, what's left of its main limit ("€1,960 left
  today of €2,000", `card.hero.left_of`), and the Card's name under it.
- **The Activity:**
  - the filters as chips, the chosen one inverted;
  - the days, "TODAY · NOV 15";
  - each line as the Record draws it: its dot (green done, approved or started; amber held, sending or unknown; red
    refused, not done or revoked), what it was, the words under it, and its time, with the money it moved when tafs
    cloud says (`amount_text`);
  - "Nothing yet." under All with nothing to show, "Nothing here." once narrowed, and tafs cloud's `end_words` under
    the last line.

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

**Not yet:**
- **Reading it from tafs cloud:** with its revision as If-None-Match, the next pages, and Try again.
- **Opening a taf from the sidebar's Your tafs and the Overview,** once they list the tafs.
- **The other tabs,** each in its own change: Setup (from `taf-setup.json`, `taf-setup-connected.json` and
  `taf-setup-reconnect.json`), the Card, and Chat.
- **The Card bar's chevron:** it opens the Card tab, so it comes with it.
- **The rest:** the head's second line (the version and Share); held calls, Card-unit exceptions, its work and its runs
  at the top of the Activity; a line's receipt and its next step.
