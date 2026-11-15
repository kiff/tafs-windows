# Coach mode

Coach mode is the iPad's `CoachPanel`, as in the reference set's `coach.png`. It's where tafs are set up, connected and
changed, over whatever is open. The core is `Tafs.Core.Coach.CoachScreen`; the window hosts `CoachPanel`.

- **The panel:**
  - It slides in from the right, a third of the window wide, over what's open (which stays in view), the sidebar too.
    It eases out over 0.22 s, as the iPad's, and slides back out when it closes, while the backdrop fades.
    With Windows' animations off (Settings › Accessibility › Visual effects), it fades instead, as the iPad's does with Reduce Motion.
  - It's on the background, with a soft shadow at its left edge (the iPad's: black at 18%, 24 out).
    A 16-wide strip down its left holds the handle. Coach's own wash is only under the intro, padded 20.
  - Behind it, a backdrop of black at 12% closes it on a tap, as Back and Escape do.
  - ⤢ makes it full screen and brings it back to a third, its edge moving over the same 0.22 s.
  - Focus moves to Back when it opens, and back to the sidebar's Coach when it closes.
- **The toolbar:** on the background, as the iPad's bar. Full screen and Back, in Coach's blue, on a pill; the title "Coach" in the middle.
- **Before Coach starts,** the iPad's intro (`SetupChatView.introBefore`):
  - "Coach mode", 30 bold, in Coach's blue;
  - the line naming how this PC asks ("You approve with Windows Hello");
  - Start, as the iPad's primary button while it's off: the dim at 22%, the soft ink, 54 high, corners of 16;
  - the footnote "Starting asks for Windows Hello once."
- **Windows Hello** is the product's own name, the same in every language, as the iPad names Face ID or Touch ID.
  The word book has no word for it, so it's `CoachScreen.Biometry`.

## Coach's thread

Signed in, opening Coach reads its newest conversation (`CoachChat.LoadAsync`, from `AppModel.loadSetupThread`), through
Home's read, so the token goes only to the exact hosts: `GET /v1/setup` for Coach's id, `GET /v1/tafs/{id}/chats`, then
the newest by update's `GET /v1/tafs/{id}/conversation/{session}` (the whole thread without a list). The iPad's
`X-Time-Zone`, which can set a new account's zone, is left out, so opening Coach changes nothing.
Also: times are in this PC's zone, not the account's (not read yet); a `working` without its session reads as not at
work, where the iPad's decoder fails the thread.
- **Read as the iPad's decoders:** a message without its time, sender, text or session fails the thread, while work or a
  line it can't read is left off; a list entry without its session, title, start or update fails the list.
- **No Coach, or `/v1/setup` unread:** the intro stays. A conversation that can't be read or decoded says so under the
  intro, red at 13: "The conversation couldn't be loaded:" and why (for one that doesn't decode, the app's own words).
- **Drawn as `SetupChatView.thread`** (`CoachPanel.ShowThread`), each message once, in order: a time between parts an hour
  apart (`gapTime`); the person's words on the right in the brand bubble, an answer as what they chose (`ownerWords`), with
  "Answered the card above"; Coach's words full width at 16 under "Worked for 12 s ›", which opens the steps in Coach's
  words (`WorkLog.rows`: "2 tries", failures only when nothing worked); a run's start as a centred line of two lines at
  most; then **While you were away** and the handed tasks' lines ("↳ Rae: …").
- **Coach's cards** are read with the thread, as the iPad's `SetupThread`: one it can't read is left out; one shown again
  replaces itself in place; a card its run waits on goes after Coach's words with it. Each field the iPad declares is read
  with its decoder's shapes: absent or null is none, another shape leaves the card out; an answer whose state or words
  aren't words leaves the card unanswered. A kind not drawn yet is left out, and
  an answer to one stays in its bubble, in the person's words ("Approve", a confirm's option).
- **A questions card** (`QuestionsCardView`, as `QuestionsCard`): Coach's words, its title as an eyebrow ("Setup needs"
  without one), "0 of 2 answered", each question numbered with its choices in a wrapping row (`WrapRow`, as `FlowRow`), then
  "Something else…", which opens a field for the person's own words; a multi question takes several. Send answers (the brand
  button) once every question is answered; Chat about this puts the card away ("You're talking these questions through…",
  Show them again). Answered, it's one line, "Your answers · Test mode · Weekdays", and the answer's bubble is folded in.
- **A notice** (as `SetupComponentView.notice`): its title at 14 in its tone's colour (error the red, warning the amber,
  success the green, else soft), its words in the ink, on that colour at 10%.
- **An approval** (as `SetupApproval`), its Approve and Decline off until approving is ported:
  - "NEEDS YOU · APPROVE", and planque's own words for the connection, read from planque as this device with its digest
    checked (`PlanqueGrants`); its exact terms under Details; "This request doesn't match what it shows" when the digest
    doesn't; "Reading the request…" until it's read. A connection already connected says so in one line (tafs cloud's
    `connection`, else "Stripe is connected").
  - The taf's Card as KIFF drafted it (`KiffDrafts`), its title and lines; by tafs cloud's mark (`signs`), as the Card
    approved next, under its words and dimmed.
  - Where it stands (`ApprovalCard.State`): done only by its answer in the thread, never by planque's state alone;
    declined; didn't go through, with why, or planque's reason when it failed the grant.
  - Answered, one line ("Approved · Refunds' Card", "Declined · Stripe"), with the Card KIFF signed under it. A newer
    request for the same connection folds the earlier one ("Approved · Setup asked for more below").
  - Not yet: KIFF's words for a connection's profile (it says they aren't read), opening a folded card again, and the
    key sheet.
- **A confirm** (as `SetupComponentView.confirm`): Coach's question and its options as capsules; a tap answers the card with
  the option's value, as a questions card's answers go, and the options fade while it's sent. Answered, one line: "Live or
  test? · Test mode".
- **A result** (as `SetupComponentView.result`), on the panel with a green edge, its eyebrow in the green:
  - **a new taf's hand-over:** "SET UP · RAE", Coach's words, its tools in words (`toolWords`), then by tafs cloud's
    outcome: set up, Open (or Coach's own words, else "See it on Rae"), which closes Coach and opens the taf's page; with
    `plan_needed`, its words in the amber and Choose a plan, which opens Plan & usage; not set up, why in the red and "Coach
    knows, and hands it over again."; else "Setting up Rae…". The taf is found by its KIFF agent in Home's list.
  - **an improvement** (it names the taf it improves): "NEW VERSION · RAE", Coach's words and what changed, then "Version 3
    is made.", why it wasn't, or Preparing the new version… on a button that's off.
- **The plan** (as `SetupPlan`), pinned above the thread, never in it: "PLAN · 1 OF 4" and the step it's at with its mark
  (`StepMark`: a tick on the green, a cross on the red, a ring in the amber at work, a faint ring to do). A tap opens every
  step, the active one in bold, a skipped one struck through, those to come dimmed; another folds it. Narrator reads a
  mark's status as tafs cloud words it ("done"), as the iPad's does.
- **Who may answer** (`CoachAccess`, as `AppModel.mayUseCoach`): KIFF's access to Setup's agent (`GET /v1/me/access`),
  else the account's `members.manage` capability (`GET /v1/me`); nothing read is no. Without it, Send stays off.
- **Sending** (`AnswerAsync`, as `AppModel.answer`): `POST /v1/tafs/{id}/components/{card}/answer` with
  `{"answer": {…}, "session": …}`, only to the exact hosts, allowing 70 seconds, for the card waiting under that id
  (`cardRef`), every card under it busy meanwhile. Sent only when tafs cloud answers its session, run and status.
  Signing out drops the thread, what was answered and an answer in flight. Sent, the thread is read again; a 409
  (the card doesn't wait any more) reads it again too; refused for want of the AI-data consent (`ai_consent_required`), the
  card stays as chosen; anything else says "Your answer didn't reach Setup. Tap it again." under the card.
- `--home` with `--page coach` reads tafs cloud's contracts beside the Home file: `setup-coach.json`, `coach-chats.json`
  and `coach-conversation.json`. CI saves Coach's thread that way (`coach-home`), and `CoachContractTests` read them.

## Reporting an answer (`AnswerReport`, `ReportAnswerDialog`)

Ported from the iPad's `ReportAnswerSheet` and `reportable`, `AppModel.canReport`/`reportAnswer`/`ReportOutcome` and
`TafsCloud.ReportReason`/`reportAnswer` (kiff/tafs#421, Google Play's rule for content an AI generates). tafs-macos #77 is
a second reference.
- **Which answers:** Coach's, with words, not a run's start, with tafs cloud's id, for a KIFF account. Right-click (or
  the menu key) on one gives **Report this answer**, with its flag. Copy stays on Ctrl+C.
- **The dialog:** "What's wrong with it?" with Offensive, Harmful, Wrong and Other; the note ("Add a note (optional)"),
  with the red line past 1,000 characters (as a person counts them); the footer, that a person at KIFF reads it.
  Send is off with no reason, while sending, or past the limit.
- **The request:** `POST /v1/tafs/{coach}/messages/{id}/report` with `reason`, the note trimmed (left out when blank)
  and the answer's `session`.
- **What it comes to:** `{"report": {"id", "at", "new"}}`: new says "Thanks. Someone at KIFF will read your report.",
  else that it was reported already, each with Done. Not sent, it stays open with "Not sent:" and tafs cloud's `words`,
  or that the answer isn't on tafs cloud any more (404), or "It didn't reach KIFF. Try again.", never tafs cloud's codes.
  One answered after the person signed out says "You signed out."
- **Not yet:** a taf's and a trial's answers, whose chats aren't on Windows yet.

**Not yet:**
- **Coach's words as Markdown:** they show as written until `MarkdownText` is ported.
- **A Coach with an empty thread:** the iPad shows `starts` ("Ask anything"); here the intro stays until writing is ported.
- **See version 3,** which opens the taf's Versions on the iPad: Versions aren't ported, so the made line shows instead,
  as the iPad does when it can't find the taf.
- **Coach's other cards** (sign-ins), approving, and the conversation read's proof and `?can=`
  (they mark approval cards).
- **After an answer:** the iPad follows the run's stream as it writes; here the thread is read again, until the stream is
  ported. The AI-data consent's screen isn't ported, so a refusal for it leaves the card as it was, with no line.
- **A drive's end** (what Coach did in the app, `DriveRecap`), placed in the thread on the iPad: with drives.
- **Writing to Coach:** the composer, sending, the stream, Stop, unsent and queued messages, steering, reading again.
- **Starting Coach:** Start stays off until it's wired.
  - tafs cloud's first start (its own words and the tools Coach is given, signed under one Windows Hello prompt);
  - then the recipes, New conversation and the previous conversations (the iPad's ✎ and ⋯);
  - "Taking shape" beside a wide chat.
- **Dragging the edge** to choose the panel's width, and keeping it and full screen for the next time.
- **The backdrop as a button** for Narrator: it has its name, but a tap is the only way to use it. Back and Escape close Coach from the keyboard.
