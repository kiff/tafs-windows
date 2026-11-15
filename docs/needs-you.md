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

This PC doesn't approve yet: the Windows Hello calls are their own change. So where the iPad has
Approve and Refuse, the card says why this PC can't and offers **Add this Windows PC**, as the
iPad's own gate does when a device can't approve: words, never buttons that can't work.

## Where the calls come from

Reading the held calls from KIFF needs signed requests, which come with the approvals. Until then
Needs you is empty ("Nothing is waiting."), and `--sample` shows the iPad's two sample calls for
screenshots: Deploys' migration and Bill pay's €8,400. Their words are the shared sample words, so
they're in every shipped language. Needs you's row in the sidebar carries their count at its end, as
the iPad's sidebar list draws a row's badge: a plain number, 15 regular, in the soft colour (not the
tab bar's red pill). The row is named with it: "Needs you, 2".

The page is capped at 760 wide, from the leading edge. The iPad's has no cap; on a wide window, cards
that wide would put the amount far from the answers.
