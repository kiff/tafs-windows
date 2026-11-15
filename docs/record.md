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
- **The filters' chips,** by the iPad's rules (`RecordFilter.keeps`):
  - All;
  - Money moved: an amount that went, done or approved;
  - Stopped: refused, not done or revoked;
  - Waiting: held, sending or unknown.
  A taf's start shows under All only.
- **The chips:** 13 in a capsule, on the panel with a line around. The chosen one inverts: the ink's
  fill with the ground's colour on it. They have their own template, so pointing at the chosen one never
  washes its colours out.
- **Each day:** its eyebrow, "TODAY · NOV 15", "YESTERDAY · NOV 14", else the weekday and date, 11 medium
  monospaced, in capitals by the language's own rules. Then its lines.
- **Each line,** on a panel with corners of 14:
  - the status dot: green done, approved or started; amber held, sending or unknown; red refused, not
    done or revoked. Those are tafs cloud's kinds (`done`, `approved`, `held`, `refused`, `not_done`,
    `sending`, `unknown`) and KIFF's `revoked`. Any other value reads as unknown, so it waits under
    Waiting rather than passing as done (`EntryKinds.Parse`);
  - the taf's dot and "Chargebacks · Accept dispute on #3342" in 14 semibold;
  - the time, "00:17", 12 monospaced, 24-hour, this PC's;
  - the detail in 13 soft: "€35, within the Card", the rule that held it, or "Card issued by You".
- **With nothing to show:** "Nothing yet." for the whole Record, and "Nothing here." once it's narrowed.

## Where the lines come from

Signed in, the lines are KIFF's activity (`KiffActivity`), read with each refresh of Home's list, as
the iPad's `refresh` does:

- **The read:** `GET /v1/me/activity?limit=100` on KIFF, as the session (`KIFFClient.activity`). It's
  read as strictly as the iPad's decoder (`ActivityCall`): every field it requires, in its shape, and any
  other absent, null or of its shape; times in ISO 8601 with their offset. One call it can't read fails
  the whole read, and a read that fails leaves the Record as it was. One that lands after another
  sign-in is dropped. A sign-out empties it.
- **Whose calls:** only those of a taf on Home's list, matched by the taf's agent in KIFF
  (`kiff_agent`), in KIFF's order (`KIFFMapping.record`).
- **What KIFF decided:** allowed; held for the person ("Waiting for you"); anything else refused, in
  KIFF's words when it has them ("Not sent · over the €200.00 limit for one refund").
- **What the tool did,** for an allowed call (`KIFFMapping.executed`), kept apart from the decision: done
  only when the tool said so ("Done, within its Card", "Approved by you, then done"); refused by the tool,
  saying it still counts against the Card when it moved money or another unit; not sent, sending, answer
  lost, or not recorded. A status this app doesn't know reads as not recorded, never done. A connection
  that refused it before sending says so.
- **The title:** the tool's title from KIFF, else the action in words, then its amount: "Refund a payment
  · €150" (`titleWithAmount`). Money is written as the app writes it everywhere; another unit as KIFF
  wrote it ("3 seats"); a value that isn't a number, none. Money moved counts it.
- **The detail,** once there's an answer or a receipt: who decided and what became of it, "approved by
  you · done", "refused by an admin · nothing sent" (`decidedLine`, `resultLine`). Otherwise what the tool
  did.

For a screenshot, `--home` reads `kiff-activity.json` beside the Home file, if there is one, in place
of KIFF. With neither, the Record is empty; `--sample` shows the reference set's Record in every
shipped language: what the five sample tafs did today, the two held calls, and each taf started with its
Card. The sample's words are the shared sample words.

## Not yet

- **The tafs' chips are the tafs in the lines.** On the iPad they're the account's tafs, so a taf with no
  line yet still has its chip. They switch when the tafs list arrives.
- **A line's receipt.** On the iPad, a tap opens KIFF's receipt; here, lines don't open yet.
- **From the iPad's Record, still to come:**
  - what happened in tafs itself beside KIFF's calls (its events);
  - answers given on this PC folded in (`withDecisions`);
  - what a call acted on, by its tafs' call refs and KIFF's hash ("payment …QbstR", `onText`);
  - a held call KIFF no longer holds reading "no longer waiting";
  - the vendor's name when its tool refused a call (it says "The app" for now).
- **No contract for KIFF's activity yet.** The tests use answers in the iPad decoder's shape.
- **Search and export**, which the iPad shows when signed in.
- **Reads folded into one line a day,** and the day's "€640 spent" in its eyebrow: both need the real
  lines.
