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
  wrote it ("3 seats"); a value that isn't a number, or can't be written as the money it is (more than
  18 decimals, or more minor units than a whole number holds), none: never a crash on KIFF's answer. Money moved counts it.
- **The detail,** once there's an answer or a receipt: who decided and what became of it, "approved by
  you · done", "refused by an admin · nothing sent" (`decidedLine`, `resultLine`). Otherwise what the tool
  did.

For a screenshot, `--home` reads `kiff-activity.json` beside the Home file, if there is one, in place
of KIFF. With neither, the Record is empty; `--sample` shows the reference set's Record in every
shipped language: what the five sample tafs did today, the two held calls, and each taf started with its
Card. The sample's words are the shared sample words.

## Export receipts (`ReceiptsExport`)

As the iPad's `ExportReceiptsRow` and `exportReceipts`, for an accountant, under the Record's lead, only for someone who
sees every taf's Record (every taf on Home managed by them, as KIFF says; tafs cloud still decides, 403):
- **Export receipts** opens the ranges: the last 7, 30 or 90 days, today among them (`GET /v1/record/receipts?from=&to=`,
  92 days at most). One exports them, "Preparing receipts…" meanwhile.
- **Two files**, `receipts-{from}-to-{to}.json` and `.csv`, shared through Windows' share sheet: the JSON is tafs
  cloud's answer byte for byte, so KIFF's receipts are exactly as issued, never re-encoded; the CSV as tafs cloud wrote
  it. How many calls have no receipt yet is said under the row. They're written under the temporary folder, replaced at
  each export and removed at every session change, as Export my chats' are.
- Not exported: "They couldn't be exported." and why, in red.
- For a screenshot, `--home` shows the row, answered from a `receipts-export.json` beside the Home file.

## A line's sheet

A tap on a line opens its sheet (`ReceiptSheet`), as the iPad's `ReceiptView`; Narrator reads the line
as one, with "Shows the receipt". In `Tafs.Core.Record` (`Receipt`):

- **The head:** the dot and who answered it ("Approved once by an admin"), else what became of it
  ("Done within the Card"), then the title with its amount at 30 bold.
- **The facts:** Taf; When, with seconds; Outcome ("Done. €150 moved.", "Not sent. Its Card refused
  it."); Why, in KIFF's words when it said; Decided by ("You, approving once", "An admin, signed on iPad",
  "KIFF, against the Card", or "The Card" for the sample's lines).
- **KIFF's receipt,** for a line that has one: `GET /v1/receipts/{trace_id}` on KIFF, as the session,
  kept as KIFF wrote it. Its chain (`causal_chain`) is taken as written, never decoded and written again
  (`RawJson`, as the iPad's `RawJSON`), since that's what KIFF signed.
  - **Checked on this PC** (`ReceiptCheck`, as iOS's and Android's): the signed digest is
    keccak256(ascii(sha256hex(causal_chain))), and the signer recovered from the 65-byte secp256k1
    signature must be the one the receipt declares and KIFF's (`ReceiptCheck.KiffSigner`, one key for
    every account; a rotation ships in a new build first). A receipt that doesn't read, or writes a key
    twice at its top or in its attestation, isn't one KIFF wrote. Keccak-256 and the key's recovery are
    BouncyCastle's (`BouncyCastle.Cryptography`, pinned to 2.5.1 with its hash in Tafs.Core's
    `packages.lock.json`, which CI restores locked), the library Android's check uses. While it's read
    and checked: "Checking KIFF's receipt on this Windows PC…".
  - **What happened:** each step KIFF signed, its message (else its kind in words) and its time, on a
    line of dots, drawn only once the receipt checks out here and matches the line
    (`Receipt.ShowsWhatHappened`).
  - **The receipt's panel,** by the check:
    - **Checked:** "KIFF's receipt · checked on this Windows PC" in green, "Signed by KIFF, unchanged
      since.", the decision in the receipt's words and when it was issued ("held → approved once by you ·
      issued …"), **Show the full receipt** (its id and signer shortened, each step with its hash, and
      the signature), and Copy receipt.
    - **Not signed yet:** "KIFF's receipt · not signed yet", KIFF signs within a minute; after ten
      minutes, "If this stays, tell us the receipt id below." with its id.
    - **Doesn't check out:** "KIFF's receipt doesn't check out", "This receipt was changed after KIFF
      signed it, or it wasn't signed by KIFF. Don't rely on this line.", the facts, the full receipt with
      no signer, and Copy.
  - **A line the steps contradict,** once checked, says KIFF's receipt doesn't check out, and why: the decision step
    signed another amount, or the hold was settled with another answer (KIFF writes a refusal "reject").
    Amounts are compared as numbers with their unit, so "150.00" and "150" agree. The iPad compares
    the strings, which would call that a contradiction. One that isn't a number is compared as written.
  - **Not read:** why, and Try again.
- For a screenshot, `--home` reads `kiff-receipt.json` beside the Home file as the receipt.

## Not yet

- **The tafs' chips are the tafs in the lines.** On the iPad they're the account's tafs, so a taf with no
  line yet still has its chip. They switch when the tafs list arrives.
- **Share** beside Copy on a checked receipt, as the iPad's ShareLink.
- **Open in the taf's chat,** at the sheet's foot on the iPad.
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
