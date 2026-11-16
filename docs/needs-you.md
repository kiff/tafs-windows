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
- **The gate stands exactly while this PC can't approve**, as the iPad's `ApprovalGate`: only a Passkey session that
  knows its passkey approves (`HeldAnswers.Approves`), so a Key session, a passkey session with no id, a view-only one or
  none keep it.

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

## Answering a held call (`HeldAnswers`), the signing

Ported from the iPad's `AppModel.answer(_:with:)`, `kiffAnswer`, `canAnswer`, `lastingRefusal`, `refusesOnly` and
`KIFFClient.answerMessage`/`approveOnce`/`reject`/`changeCard`, signed as KIFF's desktop spec says (§2.5, §2.6). The
card's buttons come with the passkey's wiring; this is what they send.
- **The message:** `kiff.answer.v1`: tenant, the hold's id, `approve-once`, `reject` (a next step too) or
  `change-card`, the next step or empty, sha256 of the note in hex or empty, KIFF's accounting version for the hold, and
  signed-at.
- **Which key:** approving once and extra room grant authority, so this PC's passkey signs them. One fresh Windows Hello
  assertion, its challenge base64url(sha256) of the message, checked before anything is sent: kiff.dev, the person
  verified, a passkey that can't leave this PC. It goes as `"signature": {"signed_at", "passkey": {credential_id,
  client_data_json, authenticator_data, signature}}`. A refusal, with a next step or not, grants nothing: the device key
  (TPM) signs it, as `"signature": {"signed_at", "value"}`.
- **The routes:** `POST /v1/me/card-exceptions/{id}/approve-once`, `…/reject` (with `next_step`) and `…/change-card`.
- **Who answers here:** the hold came from KIFF with its version, the tenant is known, and the session's device key is
  here. Approving takes a passkey session with this PC's own passkey besides.
- **What it comes to:** closing Windows Hello is "Cancelled, so nothing was approved…"; an assertion KIFF would refuse is
  "Not sent: it wasn't confirmed."; either way nothing is sent and the call stays. KIFF's refusal is worded as the iPad's
  (`self_review`, `role_forbidden`…), and a lasting one is kept for the card: `self_review` leaves Refuse and the next
  steps, any other takes the answers away. A sign-out at any point sends nothing more.

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
