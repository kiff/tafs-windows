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

Signed in, opening Coach reads its newest conversation (`CoachChat.LoadAsync`, ported from `AppModel.loadSetupThread` and
`TafsCloud.setupThread`), through the same read as Home's, so the session's token goes only to the exact hosts:
- `GET /v1/setup` gives Coach's id; none, and the intro stays. The iPad sends `X-Time-Zone` with it, which can set a new
  account's zone in KIFF; this read leaves it out, so opening Coach changes nothing.
- `GET /v1/tafs/{id}/chats` gives its conversations, and the newest by when it was updated is read:
  `GET /v1/tafs/{id}/conversation/{session}` (the whole thread without a list).
- **Read as the iPad's decoder:** a message without its time, sender, text or session fails the thread; work or a line it
  can't read is left off. The list of conversations is read as strictly: an entry without its session, title, start or
  update fails the list, and the whole thread is read instead.
- **A thread with messages** shows in place of the intro. With no Coach, or `/v1/setup` unread, the intro stays as it is.
  A conversation that can't be read, or doesn't decode, says so under the intro in red at 13: "The conversation couldn't be
  loaded:" and why (tafs cloud's words, else its error, else its status; for one that doesn't decode, the app's own
  "didn't understand each other", where the iPad shows the system's words).

**Drawn as `SetupChatView.thread`** (`CoachPanel.ShowThread`), each message once, in the order they happened:
- **A time** between parts an hour or more apart (`gapTime`): today's alone ("14:05"), an earlier one with its date.
- **The person's words** on the right, in the brand bubble (15, white, corners of 18), in their own words: an answer to a card
  reads as what they chose ("Approve", "Decline", "Not now", `ownerWords`); "Answered the card above" under an answer.
- **Coach's words** full width at 16, with "Worked for 12 s ›" above them when it worked (`WorkSummary(coach: true)`): it
  opens the steps in Coach's own words (`activityWords`: "Connecting an app", never a tool's name), what it showed left
  out, a step tried again after a failure as one row with "2 tries", failures kept only when nothing worked (`WorkLog.rows`).
- **A run's start** as a centred line, two lines at most, with its work above it; then **While you were away** (what ended meanwhile, on the raised colour) and how
  the tasks Coach handed tafs went ("↳ Rae: …").
- `--home` with `--page coach` reads `coach-setup.json`, `coach-chats.json` and `coach-conversation.json` beside the Home
  file. tafs cloud has no contract for these reads yet; the tests use answers shaped as the iPad's decoders.

**Not yet:**
- **Coach's words as Markdown:** they show as written until `MarkdownText` is ported.
- **A Coach with an empty thread:** the iPad then shows `starts` ("Ask anything"); here the intro stays, until writing to
  Coach is ported.
- **The conversation read's proof and `?can=`,** which only mark approval cards with what their fingerprint signs (`signs`):
  with the approval cards.
- **Coach's cards** (questions, notices, results, approvals and sign-ins), and an owner's answer folded into its card.
- **Writing to Coach:** the composer, sending, the stream (what it's doing live, the dots), Stop, unsent and queued
  messages, steering, and reading the thread again while it works.
- **Starting Coach:** Start stays off until it's wired.
  - tafs cloud's first start (its own words and the tools Coach is given, signed under one Windows Hello prompt);
  - then the recipes, New conversation and the previous conversations (the iPad's ✎ and ⋯);
  - "Taking shape" beside a wide chat.
- **Dragging the edge** to choose the panel's width, and keeping it and full screen for the next time.
- **The backdrop as a button** for Narrator: it has its name, but a tap is the only way to use it. Back and Escape close Coach from the keyboard.
