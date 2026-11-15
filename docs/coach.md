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
Two more differences: times (between parts, and a run's start) are in this PC's zone, where the iPad uses the account's,
which this app doesn't read yet; and a `working` without its session reads as not at work, where the iPad's decoder fails
the thread.
- **Read as the iPad's decoders:** a message without its time, sender, text or session fails the thread, while work or a
  line it can't read is left off; a list entry without its session, title, start or update fails the list.
- **No Coach, or `/v1/setup` unread:** the intro stays. A conversation that can't be read or decoded says so under the
  intro, red at 13: "The conversation couldn't be loaded:" and why (for one that doesn't decode, the app's own words).
- **Drawn as `SetupChatView.thread`** (`CoachPanel.ShowThread`), each message once, in order: a time between parts an hour
  apart (`gapTime`); the person's words on the right in the brand bubble, an answer as what they chose (`ownerWords`), with
  "Answered the card above"; Coach's words full width at 16 under "Worked for 12 s ›", which opens the steps in Coach's
  words (`WorkLog.rows`: "2 tries", failures only when nothing worked); a run's start as a centred line of two lines at
  most; then **While you were away** and the handed tasks' lines ("↳ Rae: …").
- `--home` with `--page coach` reads `coach-setup.json`, `coach-chats.json` and `coach-conversation.json` beside the Home
  file. tafs cloud has no contract for these reads yet; the tests use answers shaped as the iPad's decoders.

**Not yet:**
- **Coach's words as Markdown:** they show as written until `MarkdownText` is ported.
- **A Coach with an empty thread:** the iPad shows `starts` ("Ask anything"); here the intro stays until writing is ported.
- **Coach's cards**, an answer folded into its card, and the conversation read's proof and `?can=` (they mark approval cards).
- **Writing to Coach:** the composer, sending, the stream, Stop, unsent and queued messages, steering, reading again.
- **Starting Coach:** Start stays off until it's wired.
  - tafs cloud's first start (its own words and the tools Coach is given, signed under one Windows Hello prompt);
  - then the recipes, New conversation and the previous conversations (the iPad's ✎ and ⋯);
  - "Taking shape" beside a wide chat.
- **Dragging the edge** to choose the panel's width, and keeping it and full screen for the next time.
- **The backdrop as a button** for Narrator: it has its name, but a tap is the only way to use it. Back and Escape close Coach from the keyboard.
