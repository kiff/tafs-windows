# The sidebar and the Overview

The iPad's sidebar for the whole app (`HomeSplitView`, on a full-width iPad), and its Overview
(`OverviewView`), on Windows. What they say is in `Tafs.Core.Home` (`Sidebar`, `OverviewScreen`) and
tested there (the `home` part). The app only places it.

## The sidebar

From the top:

- **The mascot and "tafs"**: the way to the Overview, and what opens first. There's no Overview row
  beside them.
- **The app's screens**, in the iPad's order: Needs you, Record, Calendar, Plan & usage, Connect apps.
  Calendar is one of them, so the Overview draws no calendar button (the iPad's fix of 14 Nov).
- **NEEDS YOU**, when calls are held: its label in the amber, then each call on one row, an 8-point
  amber dot, "Deploys: Run migration…" (14 semibold) and "Schema migrations: always asks you · answer by
  00:27" (12, soft), each cut short with an ellipsis. A row opens Needs you. With nothing held, the
  section isn't there.
- **Your tafs**: its label is the iPad's eyebrow (monospaced, 12, tracked, in capitals by the
  language's own rules). With no tafs yet, the line under it is tafs cloud's when it sends one,
  otherwise "Connect an app to set up your first taf." (or, for someone who can't use Coach, where
  shared tafs will show).
  - **Its rows** are the tafs relevant now, as the iPad's one sidebar for the whole app (`Sidebar.RelevantNow`): a call
    of its waits, a step or a knock is open, or it ran or changed in the last 24 hours. All of them are on the
    Overview.
  - **Each row:** the avatar (34), the name in 15 semibold, and what it's doing in 13 soft, on one line each. A row
    opens its taf's page.
  - **The sample's** are Deploys and Bill pay, the two a call waits on, as overview.png.
- **Coach and Account at the foot**, always in sight:
  - Coach is the ink button (light in dark, with the dark ground's colour on it), 44 high, corners
    of 14.
  - Account sits under it, with the account's name once it's known. Its icon is a person in a circle in the soft
    colour, as the iPad's `person.crop.circle`; while Account is open, the circle is filled amber.

The chosen row is a rounded fill of the ink, as the iPad's list draws it: no side marker. Rows are 15
semibold in the ink, 52 apart, with their symbols at 20 in the ink. Plan & usage's symbol is bars, as the iPad's
`chart.bar`; Connect apps' is Windows' Connect (two linked devices), the nearest to the iPad's linked app boxes. The pane is the iPad's raised colour, flush with the screen beside it.

The pane is 300 wide and never folds (`PaneDisplayMode="Left"`), so the section labels have their own
small template: WinUI's header draws its text in a fixed style.

## The Overview

Today across the tafs, one line each, each line opening its taf:

- the title at 30 bold, and the subtitle at 14 in the dim colour;
- four equal columns (TAF, NOW, CARD, LAST RESULT), headed at 11 bold, with a line between rows. They
  sit on the ground, not in a card;
- "Each line opens the taf." under the table;
- **NEEDS YOU** above the table when calls are held: the amber label, then one raised row per call
  (corners of 12), an amber dot, "Deploys · Run migration…" in 14 semibold and "answer by 00:27" at its
  end. A row opens Needs you. Times are this PC's, 24-hour, as the iPad shows them;
- up to 980 wide with its padding of 24, from the leading edge.

**A taf's line** (`OverviewScreen.SampleRows` for the sample), as the iPad's grid. The whole line opens its taf's
page, and Narrator reads it as one:
- **TAF:** the avatar (30, corners of 9) and the name in 15 semibold;
- **NOW:** "Waiting for you · Run migration 0042_orders_index" while a call of its waits, else what it did last;
- **CARD:** its Card's figure and words, "9 calls left today of 10", "€23,760 left this week of €25,000";
- **LAST RESULT:** in the soft colour, two lines at most (`TafRuns.LastResult`, as the iPad's `lastResult`). It shows
  the first line of its last run's report, its Markdown dropped. A report carries no error, so for a run that stopped
  with no answer it shows why, from the runs list. Otherwise it shows when it last ran, else "Nothing yet". A taf
  tafs cloud doesn't run, as the sample's, shows "–".

`--sample` shows the iPad's five tafs, as overview.png: Deploys and Bill pay first, since a call of each waits, then
Refunds, Chargebacks and Access. Without it, and until tafs cloud lists the tafs, it shows "No tafs yet." under the
headings.

**tafs cloud's runs** (`TafRuns`): `GET /v1/tafs/{id}/runs` (`contracts/taf-runs.json`) and one run's report,
`GET /v1/tafs/{id}/runs/{session}` (`taf-run-report.json`, `-running`, `-terminated`). The Overview reads the report of
the newest run that started. What can't be read reads as none.

## Not yet

- **+ New**, above Coach on the iPad, comes with Coach: on Windows it would have nothing to open yet.
- **Where a held call opens.** On the iPad, a held call opens its taf's page, from the sidebar and from
  the Overview, with the call at its top. Here both open Needs you, since a taf's page doesn't draw its held calls yet.
- **The held calls and the tafs** come from KIFF and tafs cloud in their own changes, with the runs and their reports
  for Last result. Until then `--sample` shows the iPad's two sample calls and five tafs.
- **Who uses a taf,** under its name on the Overview ("Support · 3 people"), for whoever manages it.
- **Coach's sparkle**: the iPad's Coach button has a sparkle (sparkles while Coach drives). Segoe
  Fluent Icons has no sparkle, so it's the closest it has, a star (U+E734).
- **The rounded type**: the iPad's titles use its rounded system face. Windows has none, so they are
  the system face, bold.
