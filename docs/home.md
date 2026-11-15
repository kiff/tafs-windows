# The sidebar and the Overview

The iPad's sidebar for the whole app (`HomeSplitView`, on a full-width iPad), and its Overview
(`OverviewView`), on Windows. What they say is in `Tafs.Core.Home` (`Sidebar`, `OverviewScreen`) and
tested there (the `home` part). The app only places it.

## The sidebar

From the top:

- **The mascot and "tafs"**: the way to the Overview, and what opens first. There's no Overview row
  beside them.
- **The app's screens**, in the iPad's order: Needs you, Record, Calendar, Plan & usage, Connect apps.
  Calendar is one of them, so the Overview draws no calendar button (the owner's 14 Nov iPad fix).
- **Your tafs**: its label is the iPad's eyebrow (monospaced, 12, tracked, in capitals by the
  language's own rules). With no tafs yet, the line under it is tafs cloud's when it sends one,
  otherwise "Connect an app to set up your first taf." (or, for someone who can't use Coach, where
  shared tafs will show).
- **Coach and Account at the foot**, always in sight:
  - Coach is the ink button (light in dark, with the dark ground's colour on it), 44 high, corners
    of 14.
  - Account sits under it, with the account's name once it's known. Its icon turns amber while
    Account is open.

The chosen row is a rounded fill of the ink, as the iPad's list draws it: no side marker. Rows are 15
semibold in the ink. The pane is the iPad's raised colour, flush with the screen beside it.

The pane is 300 wide and never folds (`PaneDisplayMode="Left"`), so the section labels have their own
small template: WinUI's header draws its text in a fixed style.

## The Overview

Today across the tafs, one line each, each line opening its taf:

- the title at 30 bold, and the subtitle at 14 in the dim colour;
- four equal columns (TAF, NOW, CARD, LAST RESULT), headed at 11 bold, with a line between rows. They
  sit on the ground, not in a card;
- "Each line opens the taf." under the table;
- up to 980 wide with its padding of 24, from the leading edge.

The tafs list comes from tafs cloud in its own change. Until then it shows "No tafs yet." under the
headings.

## Not yet

- **+ New**, above Coach on the iPad, comes with Coach: on Windows it would have nothing to open yet.
- **Needs you's count** on its row, the held calls above Your tafs, and the tafs themselves come with
  tafs cloud's list.
- **Coach's sparkle**: the iPad's Coach button has a sparkle (sparkles while Coach drives). Segoe
  Fluent Icons has no sparkle, so it's the closest it has, a star (U+E734).
- **The rounded type**: the iPad's titles use its rounded system face. Windows has none, so they are
  the system face, bold.
