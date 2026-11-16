# Calendar

The iPad's Calendar as the reference set's `calendar.png` and `calendar-week.png` show it on an upright
iPad. `Tafs.Core.Calendar` (`CalendarScreen`, the `calendar` part) says and counts; `CalendarPage` draws.

## The screen

- **"Calendar"** at the top, 17 semibold, centred.
- **The title,** in the language's order: "Sun 15 November 2026", "9 – 15 November 2026" (both months
  or years when the week crosses them), "November 2026".
- **Today,** a pill sized to its word, 15 medium with a line around; at today, quieter and inert.
- **The arrows** step a day, a week or a month (to the month's last day when it has fewer).
- **The date** on a raised pill, 17, with no glyph, as the iPad's compact DatePicker: written by Windows' formatter
  for the language, the month short ("Nov 15, 2026", "15 nov. 2026").
- **Day / Week / Month** on the system grey track (#767680 at 12%, 24% in dark), as the iPad's segmented control.
  Day's hours scroll with no second scrollbar.
- **Day / Week / Month,** the iPad's segmented control, the chosen segment lighter. It opens on Week.
- **The strip:** Week's seven days, or Day's six weeks scrolled to the selected week. Days are 66 high,
  corners of 12; the selected one amber with white words, today ringed in amber.
- **Day:** the hours, 00:00 to 23:00, a line across each, 44 high, opened at 08:00.
- **Week:** the selected day's heading as the eyebrow ("SUN 15 NOVEMBER"), and its list.
- **Month:** six weeks under the weekdays; other months' days dim, the selected one on amber at 18%,
  today ringed. Then the selected day's list.

Weeks start on Monday, on the Gregorian calendar.

## The work to come (`CalendarRead`, `CalendarWork`)

As the iPad's `TafsCloud.CalendarRead` and `CalendarMath`: `GET /v1/calendar?from=&to=` as the session, one read for the
selected month's six weeks (`CalendarWork.Range`), which draws its day, its week, the month and the navigator's marks.
tafs cloud gives each event its day, time and minute in the account's zone, so nothing here converts a zone. An event
this app can't read is left out, never the read.
- **Until the range is read,** "Reading the calendar…", and no day says it has nothing (`CalendarWork.Current`). A read
  that fails says why, with Try again. Halted, or with work left out (`more`), tafs cloud's line.
- **Marks:** each day's tafs with work, three colours at most, under its number, white on the selected amber.
- **Day (and Week when wide):** the hours, each day's work in its hour as a chip in its taf's colour ("08:00 · Morning
  inbox"; the name alone in a week), ×4 for a dense schedule's folded firings, opened at the first hour with work, else
  08:00. "Nothing scheduled on this day." or "this week." once read.
- **The day's list (compact Week and Month):** each piece of work's time (13 mono), its colour's dot, its name and tafs
  cloud's words, on a panel. A tap opens the taf, when it's on Home.

## Wide

From 700 across, wider than tall and 600 high (`CalendarScreen.LayoutOf`), as an iPad in landscape:
- **The month navigator** on the left, 280 wide on a panel: the month's name, its own previous and next month, the six
  weeks with each day's marks; a tap selects the day.
- **Beside it:** the title, Today and the arrows, the date, Day / Week / Month (320 wide), then Day and Week as hours
  (Week's seven columns under their days, a tap on one opens it in Day) and Month as a large grid, each day 64 high with
  how many firings it has.
- **The agenda** under them: the days shown, each day's heading (today in amber) over its work; tafs cloud's words when
  there's none.
- `--home` reads a `calendar.json` beside the Home file as tafs cloud's answer, for screenshots.
