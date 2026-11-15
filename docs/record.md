# The Record

The iPad's Record (`RecordView`, `RecordRow`), on Windows: everything the tafs did, were refused, or
asked, newest first, by day. The reference set's `record.png` (kiff/tafs#1054) is what it's matched
against. What it says is in `Tafs.Core.Record` (`RecordScreen`), tested in the `record` part.
`RecordPage` places it.

## The screen

- **The title** at 34 bold, and the lead under it: "Everything your tafs did, were refused, or asked
  you. Newest first."
- **The tafs' chips:** All tafs, then each taf with its colour's dot, the one started last first, as
  the iPad lists them. On a narrow window the row scrolls sideways.
- **The filters' chips:** All, Money moved (done, with an amount), Stopped (refused), Waiting (held).
- **The chips:** 13 in a capsule, on the panel with a line around. The chosen one inverts: the ink's
  fill with the ground's colour on it. They have their own template, so pointing at the chosen one never
  washes its colours out.
- **Each day:** its eyebrow, "TODAY · NOV 15", "YESTERDAY · NOV 14", else the weekday and date, 11 medium
  monospaced, in capitals by the language's own rules. Then its lines.
- **Each line,** on a panel with corners of 14:
  - the status dot: green done or started, amber waiting, red refused;
  - the taf's dot and "Chargebacks · Accept dispute on #3342" in 14 semibold;
  - the time, "00:17", 12 monospaced, 24-hour, this PC's;
  - the detail in 13 soft: "€35, within the Card", the rule that held it, or "Card issued by You".
- **With nothing to show:** "Nothing yet." for the whole Record, and "Nothing here." once it's narrowed.

## Where the lines come from

The lines come from tafs cloud and KIFF, with their receipts, in their own change. Until then the
Record is empty, and `--sample` shows the reference set's Record in every shipped language: what the
five sample tafs did today, the two held calls, and each taf started with its Card. The sample's
words are the shared sample words.

## Not yet

- **A line's receipt.** On the iPad, a tap opens KIFF's receipt; here, lines don't open yet.
- **Search and export**, which the iPad shows when signed in.
- **Reads folded into one line a day,** and the day's "€640 spent" in its eyebrow: both need the real
  lines.
