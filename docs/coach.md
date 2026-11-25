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
  - ⤢ makes it full screen and brings it back to the width it had, its edge moving over the same 0.22 s.
  - **Its width,** as the iPad's handle: the left strip drags it wider (to the left) or narrower, never below a third, the
    width it opens at. Ending within 40 of the window's width makes it full screen; ending more than 60 narrower than a
    third closes it, and it opens again at the width it had. The arrow keys on the strip do the same, a tenth of the window
    at a time, without closing it. Narrator reads it as "Coach mode's width" with "Drag to make it wider or narrower", and
    the pointer shows a resize arrow over it (`CoachScreen.Dragged`, `CoachScreen.Dragging`).
  - **Kept for the next time,** as the iPad's `coach.width` and `coach.full`: the share of the window and full screen, in
    `%LOCALAPPDATA%\tafs\coach-width` (`CoachWidth`). Anything else there opens it at a third.
  - Focus moves to Back when it opens, and back to the sidebar's Coach when it closes.
- **The toolbar:** on the background, as the iPad's bar. Full screen and Back, in Coach's blue, on a pill; the title "Coach" in the middle.
  Back's tooltip says "Close Coach mode". Once Coach is there, for someone who may use it, New conversation (✎) and Coach's
  conversations (⋯) on a pill on the right, as the iPad's.
- **Before Coach starts,** the iPad's intro (`SetupChatView.introBefore`):
  - "Coach mode", 30 bold, in Coach's blue;
  - the line naming how this PC asks ("You approve with Windows Hello");
  - Start, the iPad's primary button (below); off, as the iPad's, the dim at 22% with the soft ink, 54 high, corners of 16;
  - the footnote "Starting asks for Windows Hello once."
- **Windows Hello** is the product's own name, the same in every language, as the iPad names Face ID or Touch ID.
  The word book has no word for it, so it's `CoachScreen.Biometry`.

## Starting Coach (`CoachStart`)

Ported from the iPad's `AppModel.startSetup`, which its Start calls (`SetupChatView.introBefore`), with `setUpPlanque` and
`enrolPlanque` as its first step (kiff/tafs `ios/Tafs`, at d14217f).

- **Who:** only a passkey session that knows its passkey (`CoachStart.MayStart`). The iPad signs Setup's draft only with
  its approval key under a biometric Face ID; this PC's TPM device key asks no one, so a key session never gives that
  authority. Any other signed-in session reads the iPad's gate line where Start would be ("This device can't approve.
  Approve on another device signed in to your account.", its `ApprovalGate` in `introBefore`); if Start is reached anyway,
  nothing is read, asked or sent.
- **The order, as the iPad's:**
  1. `GET /v1/setup`, as its `setupRead`. Here it comes first, so nothing is asked for what won't be sent.
  2. **One confirmation, before anything is sent.**
     - Windows Hello, once. With no Coach yet, the one Hello is Setup's draft's own `KIFF-Passkey-Assertion`, built in
       full first (body and `signed_at`, `SigningTime.Next`'s, milliseconds kept as written in the message and in `t=`),
       as the iPad's prepared requests are. With Coach already there, it confirms over `tafs.setup.v1|{tenant}`, the
       iPad's message.
     - Closing Windows Hello sends nothing and says "Cancelled. Nothing changed." under the intro (`approval.cancelled`).
       One that doesn't verify, or a sign-out meanwhile, stops quietly, as the iPad's failed Face ID.
  3. **planque**, unless this session set it up already (`planqueReady`, kept until sign-out): `PlanqueEnrolment` with
     the session's TPM key (`keys.md`).
  4. **Setup's draft**, as the iPad's `submitNoToolsDraft`: `POST /v1/me/agent-drafts` with
     `{"agent_id":"setup","label":"Setup","no_tools":true}`. It's signed as an approval, by the passkey's assertion only:
     anything else fails closed and signs nothing.
  5. **Its key, claimed by tafs cloud**, as the iPad's `TafsCloud.startSetup`: `POST /v1/setup` with `draft_id` and
     `claim_secret`, so the key never reaches this PC.
  6. Coach's thread is read again, as the iPad's `loadSetupThread`.
- **A sign-out meanwhile** stops it: after the confirmation, after planque's enrolment and after the draft, nothing more
  is sent once another session began (the iPad's `accountGeneration`). A late answer never sets the last account's device
  id for the next session's proofs.
- **What stops it says why**, under the intro, and what comes after isn't sent:
  - planque not set up (`keys.md`'s words);
  - KIFF's refusal of the draft, "Couldn't start." with why, read as the iPad's `KIFFClient.send`: KIFF's `error`, else
    "KIFF answered 500", and a 401 as signed out of KIFF;
  - the TPM key gone or refusing, "Couldn't start." with its code;
  - an answer with no claim secret, "KIFF didn't answer as expected";
  - tafs cloud's refusal.
- **While it runs,** Start says "Starting…" and is off, as the iPad's `startingSetup`. A sign-out meanwhile drops it.

**tafs cloud's first start** (`FirstStart`, from `/v1/setup`'s `start` before Coach exists), as the iPad's
`TafsCloud.CoachStart`, `startCoachTogether` and `sendTogether`, with `RequestsTogether`:
- **Start's own screen** (`SetupChatView.startScreen`, the iPad's `oneScreen`), when tafs cloud gives its words (`lead`
  and `start`):
  - "Coach mode";
  - tafs cloud's line;
  - what's signed, as `CoachToolsGiven(start: true)`: Coach's Cards in KIFF's words (the card title, then each Card's
    title and lines), then every group and tool under the step's details, folded at first, or the batch's lines;
  - Start in tafs cloud's word;
  - `does` under it.
- **Startable, as the iPad's `startable`:**
  - it's `to_give`;
  - the draft is a `POST` to `/v1/me/agent-drafts`, not for the gateway;
  - it has KIFF's words for its Cards. Never Cards the person hasn't read;
  - and either the batch (`POST /v1/connections/batch`, for the gateway) with `kiff.requests.v1`, or the draft alone with
    `kiff.request.v1`.
- **Signed only as Start's screen draws it.** A first start without tafs cloud's words opens the iPad's tools step, which
  isn't on this PC yet. Here it asks and sends nothing, and Start turns off.
- **The pair** (`kiff.requests.v1`): the iPad signs it only with its approval key under a biometric Face ID, and this
  PC's only such key is its passkey. Its header is KIFF's passkey form for two requests under one approval,
  `KIFF-Passkey-Assertion: t=…,a=…,with=sha256:…`: the pair's `v3` header (`docs/secure-connection.md`) in its passkey
  form, its challenge base64url(sha256) of the same `kiff.requests.v1`, which KIFF's API and gateway both take. `PasskeyPairContractTests` checks each header the way
  KIFF does. How it goes:
  - Both requests are built in full, with one `signed_at` (`SigningTime.Next`'s, milliseconds kept as written), before
    anything is sent. Each body is exactly the bytes tafs cloud wrote (`GetRawText`).
  - One Windows Hello assertion signs once, its challenge base64url(sha256) of the scheme, the account, `signed_at` and
    the two `kiff.request.v1` messages' hashes, sorted. Each request carries it with the other's hash. Anything but a
    passkey session that knows its passkey fails closed.
  - Then planque, then the batch to KIFF's gateway (`https://mcp.kiff.dev`, the session's token on exactly that host),
    then the draft once the batch answered, then tafs cloud's claim. A sign-out after the signature, the enrolment, the
    batch or the draft sends nothing more.
  - A refused batch says why, read as KIFF's (`error`, else "KIFF answered 502"), and the draft isn't sent.
- **The draft alone** (every tool already connected): tafs cloud's draft in place of Setup's, signed and sent as Setup's
  is, so the one Windows Hello is its assertion. A draft with no claim secret says "Couldn't start. KIFF didn't answer as
  expected. Try again."
- **Not for Windows:** the iPad's separate approval key (`registerApprovalKey`, `approvalKeyUnheld`). A Windows session
  signs with one key.

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
- **An approval** (as `SetupApproval`):
  - "NEEDS YOU · APPROVE", and planque's own words for the connection, read from planque as this device with its digest
    checked (`PlanqueGrants`); its exact terms under Details; "This request doesn't match what it shows" when the digest
    doesn't; "Reading the request…" until it's read. A connection already connected says so in one line (tafs cloud's
    `connection`, else "Stripe is connected").
  - The taf's Card as KIFF drafted it (`KiffDrafts`), its title and lines; by tafs cloud's mark (`signs`), as the Card
    approved next, under its words and dimmed. Until it's read, Approve waits and the card says "Reading KIFF's words for
    this connection…"; when KIFF refuses the read, or answers what can't be read, it says why
    (`coach.card_draft_not_read`) with **Try again**, as the iPad's `setupDraftErrors`. A draft read before stays drawn.
  - Where it stands (`ApprovalCard.State`): done only by its answer in the thread, never by planque's state alone;
    declined; didn't go through, with why, or planque's reason when it failed the grant.
  - Answered, one line ("Approved · Refunds' Card", "Declined · Stripe"), with the Card KIFF signed under it. A newer
    request for the same connection folds the earlier one ("Approved · Setup asked for more below").
  - **Decline** is the card's own answer, `declined`, sent as any card's (`CoachChat.AnswerRequest`).
  - **Approve** (`CoachApproval.ApproveAsync`, as the iPad's `approve`) is given only once the person may use Coach (as
    the answers are), and only to a session that approves with this PC's passkey; any other reads the iPad's gate line in
    its place. Approve stays off until what's signed is drawn: the grant read, its digest matching (or the app
    connected), and the taf's Card read when the card names one. A grant with KIFF's profile words isn't approved here
    until it's connected, since those words aren't read on this PC.
    - **planque set up first:** as the iPad's `approveAsked`, nothing is sent until this session set planque up ("Start
      from Connected apps on this Windows PC first."). That isn't kept across launches here yet, so after one the person
      starts again.
    - **A request planque refused** (its grant failed) shows "Coach mode has to change this request…" in place of the
      buttons, as the iPad's `requestRefused`; the refused digests it keeps aren't on this PC yet.
    - **One approval at a time:** while one goes through, every approval card's buttons are off, as the iPad's
      `connecting`.
    - **A sign-in wider than the job**: where tafs cloud marks the card `wider: {words}` (planque couldn't
      narrow the sign-in, `usual_access`), its words show in amber after a warning mark, above the buttons, so they're read
      before anyone approves. Blank, or of another shape, is no line. tafs cloud marks it only on a conversation read that
      carries this PC's planque proof (`Planque-Device-Signature`, kiff/tafs `markSigns`, `markWider`), so Coach's
      conversation read sends it (`CoachChat.SignsRead`), as the iPad's and the Mac's: signed with the session's TPM key,
      which asks no one, since it only reads. Its chats list and `/v1/setup` go without it; with no proof (a key gone),
      the conversation reads unmarked.
  - **A sign-in card** (`SignInCard`, the iPad's `SetupChatView.signIn`): "Needs you · sign in" (in red, "Didn't go
    through · sign in", after a failure, with why), "Sign in at Gmail" (the card's service, else planque's name for the
    app, else "the vendor"; a signed-out connection, planque's words), "The vendor's own page opens. No taf sees your
    sign-in.", tafs cloud's `wider` line where it sends one, the button in Coach's words else "Open Gmail", and under it
    where it goes, always the app's own words ("Opens Gmail", else "the vendor's page"). Then **Not now**, which answers the
    card. Done once answered, or once planque's grant is connected or holds the sign-in (`listed`): "Signed in at Gmail";
    Not now: "Not now: no sign-in at Gmail". The button stays off until connecting apps comes to Windows: it would open
    the vendor's sign-in through planque.
    - **Windows Hello's dialog** doesn't show tafs cloud's `reason`, which the iPad's Face ID prompt shows; it names the
      app and the passkey, as for Start.
    - **Closed,** nothing is approved and the card says so: "Cancelled, so nothing was approved. The request is kept:
      approve again when you're ready.", as Refuse and Your step do. Start Coach's closed prompt says "Cancelled. Nothing
      changed." once that word is on the pin; until then it says nothing.
    - **tafs cloud decides first.** This PC sends what the card showed (the grant, the digest and the Card's version
      shown, its conversation) with planque's read proof (`POST …/components/{card}/approval`). If tafs cloud answers
      Coach itself (`answered`) or the card is out of date (`read_again`), the card shows its note and the thread is read
      again.
    - **Asked to sign the Card** (`cards`): the card's mark must say it signs the Card, or it's out of date. The message
      must be `tafs.cards.v1|{tenant}|{draft}|{version shown}`, rebuilt from what this PC holds, or nothing is signed
      ("…isn't this card's").
    - **One Windows Hello signs it:** this PC's passkey asserts KIFF's confirm of that version itself
      (`POST /v1/me/agent-drafts/{draft}/confirm`, `KIFF-Passkey-Assertion`), as Start signs Setup's draft. Closing
      Windows Hello sends nothing and says nothing; a refusal or a TPM error says why.
    - **What came of it is reported**, never an answer: `confirmed`, or `failed` with KIFF's words, to
      `…/approval/{ask}`. It's signed by the TPM device key (`KIFF-Device-Signature`, `kiff.request.v1` over that
      request), with a fresh read proof. tafs cloud's note then shows, and the thread is read again.
    - **Not yet on this PC:** connecting an app (planque's approve and the vendor's sign-in), its tools with the Card in
      one Windows Hello, and a key and its Card. Asked for any of them, the card says so, and nothing is signed.
    - **A sign-out meanwhile** sends nothing more and shows nothing.
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
  card stays as chosen and the consent's screen says why (Account's `ConsentDialog`); anything else says "Your answer didn't reach Setup. Tap it again." under the card.
- `--home` with `--page coach` reads tafs cloud's contracts beside the Home file: `setup-coach.json`, `coach-chats.json`
  and `coach-conversation.json`. CI saves Coach's thread that way (`coach-home`), and `CoachContractTests` read them.

## Taking shape (`CoachShape`, `CoachPanel.Shape.cs`)

As the iPad's `SetupShapePanel`: beside the conversation, once the panel is 780 across or more
(`CoachScreen.Shaping`, its edge's strip aside), a column 420 wide on Coach's deeper blue (`TafsCoachPanel`, the iPad's
`Theme.coachPanel`) says what the conversation has made so far. It needs Coach's thread and planque (the session's
grants); narrower, or without them, the conversation is alone. It's read only from what this PC already holds (the
thread, planque's grants and KIFF's drafts) and asks no server for more; the cards to answer stay in the conversation.

- **Its heading:** "Taking shape", then the taf's name from a result card, else the plan's title; with nothing yet, "As the
  conversation goes, the plan, the apps being connected and the taf's Card show here."
- **The plan:** "Plan · 2 of 4" and each step with its mark. With a plan from Coach, its steps; its step at work reads
  "Needs you" under it while a card waits on the person. With none, what Coach asked (approve, sign in, questions,
  confirm), each where it stands: done, waiting, failed, or struck through when declined or written past. The plan pinned
  above the thread isn't shown as well while the column is.
- **Tools:** each vendor a card names, from planque's grant: Connected (green, its tools in mono), Signed in or Waiting for
  you (amber), Not connected (red, with planque's reason); then "No taf sees your sign-ins."
- **The Card:** the latest draft an approval names that KIFF has shown, with tools: its title and lines in mono, a dashed
  edge and "A draft until you approve it in the conversation." until confirmed, then KIFF's green.
- It's drawn again with the thread, when the panel crosses 780, and in the panel's theme when that changes.
- `--page coach-wide` opens Coach full screen; CI saves it (`coach-wide`) from the contracts with planque's grants and a
  KIFF draft beside them (`ci/coach-shape.py`, `tests/fixtures/coach-shape`).

## Conversations and writing to Coach (`CoachChats`, `CoachSend`)

- **Which conversation shows,** as the iPad's `setupChat` and `setupFresh` (`CoachConversation`): at first the newest, by
  when it was updated (`…/chats`, then `…/conversation/{session}`); one reopened from the list, read alone by its session;
  or a new one, which has nothing to read until its first message.
- **Once Coach is there,** its conversation shows in place of the intro when it has messages, once Start started it here,
  or once a conversation is chosen. With nothing in it, what Coach is for: "Ask anything about your tafs" and "Set up a new
  taf, connect an app, or change how one works."
- **New conversation (✎),** as the iPad's `newConversation`: an empty one, its cards' state left behind. One that's already
  empty stays, with what's typed. Focus goes to the box.
- **Previous conversations (⋯),** as the iPad's `SetupChatsList`, in the panel with "‹ Back":
  - each conversation by what was asked first (two lines at most) and when it was last updated ("16 Oct 2026, 14:01", in
    this PC's zone and the app's language), the one shown marked; a click reopens it;
  - **Archive** beside each asks first ("Archive this conversation?", with the iPad's line), then
    `POST …/chats/{session}/archive`. The one shown, archived, gives way to a new conversation. Not taken: "Not archived."
    and why, at the top;
  - **Archived** at the end lists `…/chats/archived`, each reopened with a click or put back with **Put back**
    (`…/unarchive`); not taken: "Not put back." and why;
  - **Delete** (the bin beside each, in both lists) erases a conversation for good, only in tafs cloud's words: each list
    read's `delete` (`item`, `confirm_title`, `confirm_body`, `confirm_button`, `failed`), kept from the last read that sent
    them. Any word missing, or an older cloud that sends none: no Delete, and no stand-ins (`ChatDeleteWords`, as the
    iPhone's). It asks first, by name: tafs cloud's question, the conversation's title, what goes and what stays, its button or
    Cancel. Then `DELETE …/chats/{session}` (`CoachChats.DeleteAsync`), and tafs cloud decides:
    - taken, or 404 (not this person's, or already gone): it leaves the list, quietly; the one shown gives way to a new
      conversation;
    - 409 (its run works or waits on a card): it stays, with the refusal's own words;
    - anything else, the network included: it stays, with tafs cloud's `failed` line, at the top;
$1- **The box,** as the iPad's composer, for someone who may use Coach: "Ask anything about your tafs" ("Write the next
  message" while Coach works), and Send; Enter sends; Shift+Enter is a new line.
  - Checked first, as a taf's chat: nothing to send says nothing; past 2000 characters, "That's longer than 2000 characters."
  - Shown at once on the right, then `POST /v1/tafs/{coach}/messages` with `text`, and `session` for the conversation
    shown or `new_chat: true` for a new one, always with `new_chat_after_handover: true` (the iPad's `setupTarget`).
  - **Sent:** the conversation tafs cloud names (`session`) is the one shown from then on, a new one after a hand-over
    included. The thread is read again while Coach works or a sent message waits, only while the panel is open, as a
    taf's chat is (`ChatFollow`, docs/taf.md).
  - **Not taken:** it stays after the thread as not sent, as a taf's chat does (`docs/taf.md`): "Not sent · why", Edit and
    Retry, with Copy and Delete in its menu. Refused because the account is halted, it offers Resume, which closes Coach mode
    and opens Halt all. Another conversation forgets it.
- **A run that ended without an answer** (the conversation's `failed`, `RunFailed`): tafs cloud's words, in red, as the
  thread's last line, as in a taf's chat (docs/taf.md). **Try again** under it only when tafs cloud says it's safe
  (`FailedRetry`).
$1(`CoachPanel.Write`): Coach opens with them in its box to edit and send, and nothing is sent.
  In a new conversation for a job's Try it with Coach (the iPad's `handRecipeToCoach`), in the one shown for a brief's
  next move (its `startNextMove`).
- **Signed out,** it's the newest conversation again, with nothing sent or chosen here.

## Reporting an answer (`AnswerReport`, `ReportAnswerDialog`)

Ported from the iPad's `ReportAnswerSheet` and `reportable`, `AppModel.canReport`/`reportAnswer`/`ReportOutcome` and
`TafsCloud.ReportReason`/`reportAnswer` (Google Play's rule for content an AI generates).
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
- **Not yet:** a taf's and a trial's answers.

**Coach's tools still to give** (P8), as the iPad's `CoachToolsRow` and `CoachToolsSheet`, for a Coach that already exists:
- **Read** with each conversation read, from `GET /v1/setup`'s `tools` (`CoachTools.Read`): `given`, `to_give` or `taken`,
  the pair tafs cloud built (the batch for KIFF's gateway and Coach's Cards' draft, with KIFF's words for them), and the
  step's words. This PC builds none of the requests.
- **The row,** over the box, only while they're to give and only for a session that signs with this PC's passkey: tafs
  cloud's `row` words, else "Give Coach its tools".
- **The step,** opened from the row: its title and intro, each group, then under Details each tool with what it does and
  why, and Coach's Cards in KIFF's words. Give, or Not now.
- **Give** (`CoachToolsGive.GiveAsync`) reads tafs cloud again, then:
  - `given`: done; `taken`: which name holds it ("another tool in KIFF is named list_tafs"), nothing sent;
  - with the pair: one Windows Hello of this PC's passkey over both (`kiff.requests.v1`), the batch to KIFF's gateway,
    and Coach's Cards' draft only once the batch answered;
  - with Coach's Cards alone: one Windows Hello over the draft;
  - anything else is said, and nothing goes.
  Only what the step showed is signed (`CoachTools.SignsAsShown`): the read at the tap must ask for the same requests,
  byte for byte, with the same words for Coach's Cards. Otherwise nothing is signed, the step is drawn again from that
  read with "Setup changed this request: see below" above it, and Give asks again.
  Windows Hello closed says nothing. A refusal stays in the step, in KIFF's words. tafs cloud's read after says whether
  they're given, and the row goes once they are.

**Start's last steps,** as the iPad's `startSetup`:
- **KIFF's alerts** (`KiffAlerts`, the iPad's `keepKIFFAlerts`), behind Start's Windows Hello and before planque, for
  someone who may answer held calls: tafs cloud is asked whether it already holds KIFF's alert target
  (`GET /v1/alerts`). If it doesn't, KIFF points the account's held-call alerts at tafs cloud (`POST
  /v1/me/alert-targets` with tafs cloud's `/v1/kiff/alerts`, a setup call this session's TPM key signs), and the target
  it answers with goes straight to tafs cloud (`POST /v1/alerts`). Kept already, or tafs cloud not saying: nothing is
  made. A target tafs cloud didn't take waits in memory for the next Start, and is never registered twice or written
  anywhere. Best effort: none of it stops Start.
- **Coach's tools after Start:** when tafs cloud says they're still to give, the tools step opens at once, behind its own
  Windows Hello (the iPad's `coachToolsAsked`). Nothing is given inside Start's.

**The jobs in an empty conversation,** as the iPad's `SetupChatView.starts`: under "Ask Coach anything", tafs cloud's
recipes (the gallery's, `GET /v1/recipes/gallery`) by category, each category's title in capitals over its jobs, each by
what leads it (the problem it ends, else its title) over its line. A click puts the job's words in the box, to edit and
send; it never sends. Not while improving a taf, which has its own lines. Read with the gallery; gone at sign-out.

**A chat link opens only as a clean https page** (`PageLinks`, `MarkdownView`), as the phones' `PageLinkGuard`, in Coach's chat
and a taf's:
- **Any link that isn't clean `https`** (http, mailto, or an address with a space, a control or a format character, as
  written or percent-decoded), in any answer: "This link won't open" and "tafs opens only secure web pages from a chat. This
  link goes to: {address}" (`chat.link_blocked_*`), with Cancel alone.
- **A clean https link in an answer from a page Coach read** (tafs cloud's `page_turn: true`): "Open this link?" and "It came
  from a page Coach read. It goes to: {address}" (`chat.link_confirm_*`), then Open or Cancel.
- **The same in an answer still being written** (`TafPage.Steady`), which can't be told from a page turn's yet: "This answer
  is still being written. This link goes to: {address}" (`chat.link_confirm_body_live`).
- **Any other clean https link** opens in the browser at once, its address shown on hover.

The address shown is the link's real one, never its words, whole and isolated left to right (U+2066 … U+2069), so a
right-to-left sentence can't reorder it. Closed, nothing opens.

**Not yet:**
- **See version 3,** which opens the taf's Versions on the iPad: Versions aren't ported, so the made line shows instead,
  as the iPad does when it can't find the taf.
- **Opening a sign-in card's vendor page** (it comes with connecting apps), connecting an app from an approval, and the conversation read's proof and `?can=`
  (they mark approval cards).
- **After an answer:** the iPad follows the run's stream as it writes; here the thread is read again, until the stream is
  ported.
- **A drive's end** (what Coach did in the app, `DriveRecap`), placed in the thread on the iPad: with drives.
- **Writing to Coach, the rest:** the stream as Coach writes (here the thread is read again, `ChatFollow`), Stop, queued
  messages, steering, files and dictation, and `starts` and its recipes. A message refused for want of the AI-data
  consent goes back in the box, with no Not sent row, and the consent's screen says why, as the iPad's.
- **The backdrop as a button** for Narrator: it has its name, but a tap is the only way to use it. Back and Escape close Coach from the keyboard.
