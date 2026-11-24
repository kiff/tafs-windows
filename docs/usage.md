# Plan & usage

Plan & usage is the iPad's `UsageView`: the balance, then the Credits panel. Every line is tafs cloud's `words`, as it
writes them; the app adds nothing but their order. The core is `Tafs.Core.Usage.UsageScreen`; `UsagePage` draws it.

- **Panels** are the iPad's: the panel colour, corners of 18, a line around, 16 in, 16 apart.
- **The balance** (`GET /v1/usage/balance`):
  - `words` in 17 semibold;
  - a bar 5 high at `meter_pct`, clamped to 0–100, only where something bounds it;
  - `detail` in the soft colour;
  - `warning` in the tone's colour.
  - **The tone** (`state`): `ok` is calm (a green bar), `warning` is amber, and `waiting` is red (no new run starts).
    The panel's line takes the tone at 50%, and an unknown state reads `ok`.
- **The Credits panel** (`GET /v1/usage/credits`, this period or the one before):
  - the eyebrow, then Last period / This period, then the period's `words`;
  - each taf that used credits, most first, in its own panel;
  - **Coach's paid use** past its fair use: counted, in its own panel;
  - **Not counted:** its `words`, then each reason from `not_counted.lines[].words`;
  - the `note`, last.
  - It's left out when the balance says `credits_panel: false` (no plan and no run lately). A tafs cloud from before
    the flag doesn't say, and the panel shows.
- **Reading:** lenient, as on the iPad.
  - A field it can't read is left out, never the read: a `meter_pct` that isn't a number, a blank `detail`, a `raise`
    that isn't an object.
  - A balance with no `words`, or credits with no total, reads as none.

**As the session** (`UsageCalls`, `KiffUsage`, `UsagePage.Requests.cs`): signed in, the page reads everything again each
time it opens, as the iPad's `.task`: tafs cloud's balance (`GET /v1/usage/balance`) and both periods' credits
(`GET /v1/usage/credits`, `?period=previous`), KIFF's usage (`GET /v1/me/usage`) and whether this person manages billing
(`billing.manage` on KIFF's `/v1/me`). In the iPad's order:
- **The balance,** then, where tafs cloud offers Raise and the person manages billing, **Raise the limit**: its sheet shows
  tafs cloud's offer ("Raise the limit to €300") and the limit it's raised from ("The spend limit is €200 now.",
  `usage.raise_limit.from`), the signed `from_cents` in tafs cloud's `currency` (ISO 4217, at that code's own decimals,
  in the app's language; `RaiseOffer`, `Currencies`). Then Windows Hello's passkey and the device key
  ([approvals](approvals.md), Raise the limit and Decline). Without the limit or a currency: "The limit can't be raised
  from here right now." A session that can't sign: "This device can't approve. Approve on another device signed in to
  your account." Raised or not, everything is read again: after KIFF's "the limit changed", the balance offers the
  raise from the limit it is now.
- **Who bills the plan,** in one line ("Billed by KIFF").
- **A member** (no `billing.manage`): their request waiting ("You asked an admin to raise the spend limit. An admin
  answers.") with Withdraw; else the last answer, and Ask an admin where the plan lets them ask (a raise on Company, a
  pack on Solo while packs are on sale): which pack, why (optional), Ask. Primary when the balance is waiting.
- **Someone who manages billing:** members' open requests in the shared words with their reason; on a raise, **Raise the
  limit** (the balance's, which answers every open request for one), on a pack where packs sell, "Buy Solo or credits in
  the tafs app on your phone.", else the can't-approve line; and **Decline** with an optional note, signed the same way as
  Raise: Windows Hello's passkey over that very request, then this session's TPM key (`KIFF-Device-Signature`).
- KIFF's read failing is said once, in red. A change goes one at a time, then everything is read again.
- `--page usage` or `usage-admin` with `--home` reads `usage-balance.json`, `usage-credits.json` and `kiff-usage.json`
  beside the Home file.

**Not on Windows in this release:** buying Solo and packs (a PC has no store).

- **A taf's runs** (P10): a taf's row in Credits opens its Card › Usage in the period shown (`period=previous` for Last
  period), and a run that opens (`opens`) shows its own page: the request and what it did step by step.
- **A read that failed** keeps what was read before and says why in tafs cloud's words, in red: over the balance (alone when
  none was read), and under Credits' picker for the period shown. A tafs cloud without the read (404) says nothing.
