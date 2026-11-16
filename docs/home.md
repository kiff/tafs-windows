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
- **LAST RESULT:** in the soft colour, two lines at most (`TafRuns.LastResult`, ported from `OverviewView.lastResult` in
  ShareViews.swift). It shows the first line of its latest run's report, its Markdown dropped, else that run's error.
  Otherwise it shows when it last ran (Home's `last_run`), else "Nothing yet". A taf tafs cloud doesn't run, as the
  sample's, shows "–".

`--sample` shows the iPad's five tafs, as overview.png: Deploys and Bill pay first, since a call of each waits, then
Refunds, Chargebacks and Access. Without it, and with no tafs from tafs cloud, it shows "No tafs yet." under the
headings.

## Labels (`LabelChips`, `HomeLabels`)

Chips on the tafs list, for the person to filter it by, as kiff/tafs `docs/labels.md` and the iPad's `LabelChips`,
`TafLabels` and `AppModel.filterList` (iOS #1173). tafs-macos #120 is a second reference. The app never changes a label;
Coach does.
- **What Home sends:** `labels: [id]` on each taf that has any; `labels: [{id, name, color, count, waiting, headline}]`
  for the account, in the order to show them; and `labels_words.all`, the first chip's word. Each label is read on its
  own: one with no id or name, or of another shape, is left out, and `waiting` sent as yes/no reads as one. A list of
  another shape reads as none, and Home still reads. With no labelled taf, Home sends none and the Overview is as before.
- **The chips,** under NEEDS YOU and above the table: All (tafs cloud's word, else the app's "All"), two labels with
  their count and an amber dot while one of their tafs waits, then "+N", which shows the rest once clicked. The chosen
  chip is filled; clicked again, it goes back to All. They use the Record's filter chips' look.
- **Filtering:** a chip shows only its tafs, in Home's order: it never reorders. A label that's gone goes back to All.
  The Overview opens on All every time.
- **A taf's line** shows its labels under its name, in the account's order, as small grey chips: two, then "+1" (a taf
  carries at most three). Narrator reads them with the line.
- **Not here:** Your tafs in the sidebar isn't filtered, as on the iPad's sidebar. The colour token isn't drawn, as on
  the iPad's chips.
- **Screenshot:** `--home tests/fixtures/home-labels/home.json`, three tafs and three labels (CI's `overview-labels`).

## Home's list from tafs cloud

Signed in, once the window is up, the app reads `GET /v1/home` on tafs cloud (`https://cloud.tafs.kiff.dev`) as the
session, with the app's language as `Accept-Language` (`HomeList`, `contracts/home.json` and `home-paused.json`). It's
read as the iPad's `KIFFMapping.taf`:
- **What it's doing** (NOW): tafs cloud's "Next run Fri 12:30" while it works on a schedule, else the app's words for its
  state: Waiting for you, Paused, Halted · resume it from Halt, Works from its chat (no schedule), Running, Acting under
  KIFF (idle), or Card not active.
- **What went wrong:** tafs cloud's work line, when it flags it (`work.attention`) or an idle taf isn't healthy. Your tafs
  shows it where the next run would be; the Overview keeps what it's doing.
- **CARD:** the first Card's figure and words, from its Cards as tafs cloud read them from KIFF (`TafCards`). A use KIFF
  couldn't read (`calls.used: null`) is none, never zero.
- **Your tafs:** those relevant now. With tafs but none relevant now, there's no Your tafs, as on the iPad: they're all on
  the Overview. The line for no tafs shows only with none at all.
- **A taf's page** opens on its head (its state: Working, Waiting, Paused, Halted, Ready when idle, else Stopped) and
  its Card bar.
- **The headline** ("0 tafs working.") is read as tafs cloud counts it, never counted here: a taf whose work needs looking
  at is neither working nor paused. The iPad's Overview draws none, so neither does this one yet.

**Its latest run** (`TafRuns.LoadReportAsync`, ported from `AppModel.loadReport`): once Home is read, each taf's runs,
`GET /v1/tafs/{id}/runs` (`contracts/taf-runs.json`), then the report of the newest run that started,
`GET /v1/tafs/{id}/runs/{session}` (`taf-run-report.json`, `-running`, `-terminated`), all tafs at once. The Overview is
then drawn again with them.
- With no run that started, it keeps the first run's time and error.
- Runs or a report that can't be read say "Its runs couldn't be loaded." and why (`CloudRead`, as the iPad's
  `CloudError`): tafs cloud's `words`, else its `error`, else "tafs cloud answered 503".
- Unreached, the iPad shows the system's words; Windows has none for the person, so it's "It didn't reach the server.
  Try again once you're online."

An answer that can't be read, or isn't a 200, leaves the Overview as it was. A screenshot never reads tafs cloud.
`--home shared/contracts/home.json` reads the contracts instead: Home's from that file, and each taf's runs and report
from `taf-runs.json` and `taf-run-report.json` beside it (CI's `overview-home`).

## The jobs' gallery (`Gallery`, `GalleryPage`)

The iPad's `RecipeGallerySheet`, `RecipeStrip`, `RecipeRow` and `RecipeSheet`, read with Home's list from tafs cloud's
`GET /v1/recipes/gallery`. Every word, order and colour is tafs cloud's; the app has none of its own here.

- **In the sidebar,** under Your tafs, as the iPad's `HomeSplitView`:
  - **Explore jobs** (tafs cloud's `words.explore`), with or without tafs, and for anyone, since browsing creates nothing.
    None when tafs cloud sends no words or no job.
  - With tafs, **the strip** (`strip.shown`): its title, then a few jobs the account doesn't run yet, in tafs cloud's order,
    each led by the problem it ends on an edge in its category's colour; an id the gallery doesn't list is skipped.
    Then **See all jobs** (`words.see_all`, else `words.all`), unless the strip already shows every job.
- **The gallery,** opened from Explore jobs or See all jobs, titled "Explore jobs": "All jobs", a chip per category
  (in its colour, filled once picked; picked again, every job shows), then each category's jobs. A job's card has the
  problem it ends (else its title), its line, and its apps, each after its logo where tafs cloud serves one.
  - **Logos** load only from tafs cloud's own `/v1/logos/` and 32 lowercase hex (`Gallery.IsLogo`), so no one else sees
    this PC's address. A logo that doesn't load leaves the name alone.
  - **A colour** is tafs cloud's `#RRGGBB` for light and for dark, as the window's theme is; one it can't read is none.
- **A job's sheet** (a strip job opens on it): what it ends, what that costs today, its Card in zones (does on its own,
  asks first, never does; an empty zone left out), tafs cloud's line for a passed proof run and what the job reported,
  then **Try it with Coach** (`words.try`, else `labels.hand`) for someone who may use Coach. As the iPad's
  `handRecipeToCoach`, it starts a new Coach conversation with the job's `say` in the box, to edit and send; nothing is sent.
- **Signed out,** the rows go.

## Not yet

- **+ New's gallery** (the iPad's create mode, with "Say it in your own words"), and Home's recipes with no taf.

- **+ New**, above Coach on the iPad, comes with Coach: on Windows it would have nothing to open yet.
- **Where a held call opens.** On the iPad, a held call opens its taf's page, from the sidebar and from
  the Overview, with the call at its top. Here both open Needs you, since a taf's page doesn't draw its held calls yet.
- **The held calls** come from KIFF in their own change. Until then `--sample` shows the iPad's two sample calls.
- **A taf from tafs cloud's Activity, Setup and Card tabs** come with their reads; its Card tab also needs who issued it.
- **Reading Home again:** it's read at launch and after each sign-in or pairing (`UseSession`, as the iPad's
  `useSession`, which first empties the list and drops a read the last session started), and not otherwise. A sign-out
  empties it the same way (`ForgetSession`). An expired session (401) isn't told apart yet.
- **The line's colour** for what went wrong: the iPad's is amber, red when it's failing; here it's the soft colour.
- **Who uses a taf,** under its name on the Overview ("Support · 3 people"), for whoever manages it.
- **Coach's sparkle**: the iPad's Coach button has a sparkle (sparkles while Coach drives). Segoe
  Fluent Icons has no sparkle, so it's the closest it has, a star (U+E734).
- **The rounded type**: the iPad's titles use its rounded system face. Windows has none, so they are
  the system face, bold.
