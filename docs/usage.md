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

**Not yet:**
- **tafs cloud's reads.** Until then, the page shows its title only, never a balance it hasn't read; `--sample` shows the
  iPad's (Solo, 48%, and this period's and last period's credits).
- **Changing the plan:** Raise the limit (`raise`, behind an approval), buying Solo and packs, who bills the plan,
  members' requests and Ask an admin.
- **A taf's runs:** opening a taf's row shows its runs.
- **A read that failed:** tafs cloud's words for it.
