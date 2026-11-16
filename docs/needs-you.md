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
- **The gate stands only while this PC can't approve** (a view-only session, or none), as the iPad's `ApprovalGate`. A
  Key or Passkey session reads the line with no gate until its buttons come.

Words, never buttons that can't work.

## Your step (`YourSteps`, `YourStep`)

Ported from the iPad's `YourStepCard`, `TafsCloud.YourStep`, `KIFFClient.handoffMessage`/`handoff`/`answerHandoff` and
`AppModel.showSteps`/`canAnswerSteps`/`answerYourStep`/`answerRefusedWords`. tafs-macos #78 is a second reference.
- **What it is:** a step a taf may never take itself, such as approving a bank transfer, which it prepared and handed to
  its owner. Every word on it is tafs cloud's.
- **Where it comes from:** Home's `your_steps`, with each read of Home. A step needs its hand-off; every other field
  reads leniently. A step without its hand-off makes the list unreadable: on the iPad Home then fails as a whole, here
  Home's own lines stay and the steps stay as they were.
- **Its card,** first on Needs you: tafs cloud's title as the eyebrow; the taf's name, 14 semibold; what, 16 semibold;
  why, 14 soft. The vendor's page is an outlined button only when tafs cloud vouched for the link (https, its host given
  apart and the link's own, and the button's words), with the host under it, 12 mono dim. Anything else is the words
  only. **Done** and **Skip** show only when this PC can answer: the session's device key is here, and the person
  answers the account's holds (KIFF's `holds.answer`). They're off while answering. "Nothing waiting" needs no step.
- **Answering:**
  1. The hand-off is read from KIFF (`GET /v1/me/handoffs/{id}`) for the version to sign. One KIFF no longer waits on
     says it ended and leaves the list.
  2. The person confirms with Windows Hello (a face, a fingerprint or the PIN), the prompt in tafs cloud's words: "Done ·
     Approve the transfer of €1,200 to Acme". Closed, it's "Cancelled, so nothing was approved…"; Hello not set up, or
     failing, it's "Not answered: it wasn't confirmed." Either way nothing is signed or sent, and the step stays.
  3. The session's device key (the TPM key) signs KIFF's `kiff.handoff.v1` (tenant, hand-off, done or skipped, version,
     signed-at), pinned by KIFF's vector. It grants nothing, so it's never an approval.
  4. `POST /v1/me/handoffs/{id}/answer` with `{answer, signature: {signed_at, value}}`. A 409 means it ended meanwhile.
     Taken, the step leaves the list and Home is read again.
  5. A refusal says why in the app's words: KIFF's `error`, else its `reason`, among its six, else "KIFF didn't take the
     answer:" with the app's line (signed out of KIFF for a 401). It shows at the top of Needs you, in red.
  6. A sign-out at any point ends it quietly, sending nothing more.
- **Not here:** a step on its taf's page (the request view), and Needs you's count, which counts held calls only.

## Did this happen? (`DidThisHappen`, `Unproven`)

Ported from the iPad's `DidThisHappenCard` (NeedsYouView.swift), `KIFFClient.Unproven`/`didThisHappen` and
`AppModel.loadUnproven`/`mayAnswerHolds`. tafs-macos #76 is a second reference.
- **What it is:** a money call whose outcome KIFF can't prove: its answer was lost, the app refused it with nothing
  showing it had no effect, or it's one of the first calls of a new money operation. The person says whether it
  happened.
- **The read:** `GET /v1/me/did-this-happen`, a plain read as the session, when Needs you opens and with F5, as the iPad reads it on
  open and on pull-to-refresh. Only for someone who answers held calls (KIFF's `holds.answer` in `GET /v1/me`'s capabilities; an account that
  lists none may). A call needs its proposal; its title is "" and its status unknown when not said; any other field of
  the wrong kind fails the read, which keeps the list. No `calls` is none. A sign-out clears it.
- **Its card,** before the held calls: the amber edge; "Did this happen?" in amber, 13 semibold; KIFF's words for the
  call, else its title, 15 semibold; the amount only without KIFF's words (two places unless KIFF says, and "725 (no
  currency)" with no unit); why KIFF asks, in its words, 13 soft.
- **"Nothing waiting"** only with no held call and no call to say about.
- **The answers:** the iPad's Yes and No are signed like an approval (`tafs.happened.v1|…`). On a view-only PC, for
  someone who answers held calls, the card says why it can't approve and offers **Add this Windows PC**, as the iPad's
  `ApprovalGate` shows `ApprovalNotReady` only while a device can't approve. A PC that approves (a key or passkey
  session) shows neither the line nor buttons until Yes and No are ported. The line about No goes with the buttons, so
  it isn't shown.

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
`PushDelegate.open`), for someone who answers held calls (KIFF's `holds.answer` on `GET /v1/me`, none listed allows, as
the iPad's `canAnswerHolds`), once they turned alerts on for this account in Account › Alerts. With Sound off, it goes to
Notification Center without a banner or sound, as the iPad's passive alert (tafs cloud's `push.ts` `quietFor`, and
`PushDelegate.swift:81-82`):
- **Its words:**
  - "Rae needs you": a listed taf's name, "Coach" for Coach's own agent (`setup`), and "A taf needs you" for an agent no
    taf has, as the iPad's `HoldNotification.fill`;
  - then "Refund order 42, €150.00 · Over €50 each. Answer by 14:05. Nothing is sent until you approve.", answering by
    this PC's time, as every screen here shows it.
  - **While the PC is locked, only "A taf needs you"**, because Windows shows notifications on the lock screen by
    default: no taf, amount, reason or deadline, as the iPad's alert with previews hidden and Android's while locked. It
    still opens that hold. The PC counts as locked when the input desktop isn't the person's own, or can't be read.
    Locked is read when the notification is made: one shown while the PC was unlocked keeps its details in Notification
    Centre, and they show there if the PC is locked later, as a phone keeps an alert shown with previews on.
- **When it's read:** with each refresh of Home's list, and every minute while a session is in use. The iPad hears of a
  new hold by push instead, which Windows doesn't have yet. Minimised for more than ten minutes, the minute's read stops;
  the window coming back reads once.
- **Which holds:** one KIFF has held since the last read of Needs you. A session's first read notifies nothing, so opening
  tafs doesn't announce what was already waiting: it's all in Needs you. A sample is never notified. A sign-out starts
  again.
- **One per hold:** each is tagged with the SHA-256 of the hold's id (Windows keeps 64 characters), so it's never shown
  twice.
- **Gone when the hold is:** a hold no longer held at a read, answered elsewhere or lapsed, loses its notification, as the
  iPad's `removeAlerts(forHold:)`. A sign-out takes every notification of tafs's away, so the last account's calls never
  stay on a shared PC.
- **Clicking one** brings the window forward and opens Needs you with that hold's card in view, whether tafs was open or
  the click started it. The hold is kept until Needs you lists it, as the iPad's `requestedHold`: a click that started
  tafs lands on it after sign-in's first read, and one naming a hold not read yet reads KIFF again (`onHoldAlert`). A
  session starting keeps it (a click while signed out lands after signing in); only a sign-out forgets it. Starting a
  session clears tafs's notifications.
- **How:** the Windows App SDK's `AppNotificationManager`, which works for an unpackaged app. It's registered at start
  (never for a screenshot). On a PC that refuses it, there are no notifications, and Needs you still shows every hold.
- **Not yet:**
  - a notification while tafs is closed, which needs a push from tafs cloud, as the iPad's;
  - Approve and Refuse on the notification itself;
