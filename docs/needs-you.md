# Needs you

The iPad's Needs you (`NeedsYouView`, `HeldCallCard`), on Windows: each call KIFF held because it's
outside its Card, on its own card, or a panel saying nothing waits. What it says is in
`Tafs.Core.NeedsYou` (`NeedsYouScreen`), tested in the `approvals` part. `NeedsYouPage` places it.

## A held call's card

Top to bottom, as on the iPad:

- **The taf**, with its avatar (its initial on its colour, the iPad's six) and the time left,
  counted down every second: "9:37 left", "1:02:03 left" past an hour, then "expired".
- **The amount**, when the call has one, at 44 bold, in the person's language ("€8,400",
  "8 400 €").
- **What it asks**, and the taf's context under it.
- **Why it was held**, in the amber pill: KIFF's rule, such as "Over €5,000 each" or "Schema
  migrations: always asks you".
- **The taf's own words**, when it sent some, under "The taf says", drawn as they are. They never
  stand in for KIFF's amount, rule or action above: they may come from a customer's text.
- **"Nothing is sent until you approve."**, then the answers. Once it lapses, the line says KIFF
  refused it and nothing was sent, and the answers go.

The card has the iPad's amber edge (40%) and corners of 18, on the panel colour.

## The answers, for now

This PC doesn't approve yet: the Windows Hello calls are their own change. Where the answers go, the card follows the
iPad's `answers(refuseOnly:)`, with KIFF's capabilities from `GET /v1/me` (read with the held calls; none listed allows):
- **Someone without `holds.answer`** reads "Someone who approves on this account answers this. Their answer shows
  here.", and no gate.
- **A call approving once can pass** has the gate where Approve would be: why this PC can't approve, and **Add this
  Windows PC**, as the iPad's `ApprovalGate` when a device can't approve.
- **A call over a Card total** (`can_approve_once: false`) says so ("Extra room lets only this call through." for
  someone with `cards.manage`), with the gate only where a manager's Extra room would be.

Words, never buttons that can't work.

## Where the calls come from

**From KIFF** (`KiffHeld`), as the iPad's `KIFFClient.heldCalls` and `KIFFMapping.held`, with each read of Home's list:
at sign-in, when Coach asks for Home, and with **F5** on Needs you, the Windows form of the iPad's pull-to-refresh. It's
`GET /v1/me/card-exceptions?status=held`, a plain read as the session, as the iPad's.
- **Decoded as the iPad's synthesized `CardException` decoder:** a hold missing its id, subject, action, entity, status,
  breaches or time fails the read, and so does any field of the wrong kind where it's there and not null (a decimal
  parameter, a unit without its scale, a next step that isn't text, a string limit, `"kind": 1`, a time that isn't ISO
  8601 with an offset or Z). Needs you then stays as it was. Only the taf's words (left out when they aren't text, never
  the hold) and the arguments (written as text whatever they are) are read leniently. A Card unit's hold
  (`kind: card_unit`) is left to its own panel.
- **Under its holder,** as the iPad's `holderName ?? aTaf`: the taf by its agent in KIFF, from Home's list, with its
  avatar in its colour; Coach for Setup's agent; else "A taf", with no avatar. Only a taf's calls are listed in the
  sidebar's NEEDS YOU (the iPad's `HomeSplitView`); all are on Needs you and in its count.
- **What it asks:** its title, with its action in words unless the title names the action's last word (`Says`).
  planque's four letters ending a tool's name never show. With no arguments kept, the entity follows.
- **The amount:** a parameter KIFF declares a unit for, written as money; else the parameter a breach sums, as a bare
  number; else none.
- **Why:** KIFF's own words for people when it sends them; else the Card's in money ("€150.00 is over the €100.00 limit
  for one call"); else in a count ("1 call a day for each invoice id: this would be the 2nd for in_42"); else the
  breach's or the hold's reason; else what the outcome means. A call over both a one-call limit and a total, and not
  approvable once, names the total.
- **The ordinal** ("the 2nd") is written as Apple's formatter writes digits in each shipped language (1st, 1er, 2.,
  2.º, 2º, 2e). .NET has no ordinal formatter.
- **Details:** what it's on, one line an argument as KIFF keeps them ("payment_intent: pi_…"), folded under Details as
  the iPad's. Not the amount, and not KIFF's own arguments (`kiff_operation_id`, `kiff_about`, `kiff_reason`).
- **The taf's own words:** one plain line each, control and format characters and breaks as spaces, cut at 120 and 280
  with "…". They're never KIFF's amount, rule or action.
- **When it lapses:** KIFF's `expires_at`, else ten minutes after it was held.
- **Drawn again with each read:** NEEDS YOU in the sidebar, Needs you's count, and Needs you or the Overview when open.
  A sign-out clears them.
- **Not like the iPad:**
  - A read that fails, or can't be read, is silent and keeps what's shown, as the Record's. The iPad's refresh says
    "Couldn't load from KIFF" and tries again.
  - Needs you's count is the held calls'. The iPad uses Home's `needs_you` count when tafs cloud sends it; that comes
    with Card units and steps.
  - Not yet: reading again at the next expiry and on return to the app.

`--sample` shows the iPad's two sample calls for screenshots, and never reads KIFF: Deploys' migration and Bill pay's
€8,400. Their words are the shared sample words, so they're in every shipped language. Needs you's row in the sidebar carries their count at its end, as
the iPad's sidebar list draws a row's badge: a plain number, 15 regular, in the soft colour (not the
tab bar's red pill). The row is named with it: "Needs you, 2".

The page is capped at 760 wide, from the leading edge. The iPad's has no cap; on a wide window, cards
that wide would put the amount far from the answers.

## Notifications (`HoldToast`, `Toasts`)

While tafs runs, a call KIFF holds gets a Windows notification, as the iPad's hold alert (`HoldNotification.words`,
`PushDelegate.open`):
- **Its words:**
  - "Rae needs you" (or "A taf needs you");
  - then "Refund order 42, €150.00 · Over €50 each. Answer by 14:05. Nothing is sent until you approve.", answering by
    this PC's time, as every screen here shows it.
- **When it's read:** with each refresh of Home's list, and every minute while a session is in use. The iPad hears of a
  new hold by push instead, which Windows doesn't have yet. Minimised for more than ten minutes, the minute's read stops;
  the window coming back reads once.
- **Which holds:** one KIFF has held since the last read of Needs you. A session's first read notifies nothing, so opening
  tafs doesn't announce what was already waiting: it's all in Needs you. A sample is never notified. A sign-out starts
  again.
- **One per hold:** each is tagged with the hold's id, so it's never shown twice.
- **Clicking one** brings the window forward and opens Needs you with that hold's card in view, whether tafs was open or
  the click started it.
- **How:** the Windows App SDK's `AppNotificationManager`, which works for an unpackaged app. It's registered at start
  (never for a screenshot). On a PC that refuses it, there are no notifications, and Needs you still shows every hold.
- **Not yet:**
  - a notification while tafs is closed, which needs a push from tafs cloud, as the iPad's;
  - Approve and Refuse on the notification itself;
  - taking a hold's notification away once it's answered elsewhere;
