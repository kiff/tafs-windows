# Calendar

The iPad's Calendar as the reference set's `calendar.png` and `calendar-week.png` show it on an upright
iPad. `Tafs.Core.Calendar` (`CalendarScreen`, the `calendar` part) says and counts; `CalendarPage` draws.

## The screen

- **"Calendar"** at the top, 17 semibold, centred.
- **The title,** in the language's order: "Sun 15 November 2026", "9 – 15 November 2026" (both months
  or years when the week crosses them), "November 2026".
- **Today,** a pill sized to its word, 15 medium with a line around; at today, quieter and inert.
- **The arrows** step a day, a week or a month (to the month's last day when it has fewer).
- **The date** on a raised pill, written by Windows' formatter for the language, the month in full.
- **Day / Week / Month,** the iPad's segmented control, the chosen segment lighter. It opens on Week.
- **The strip:** Week's seven days, or Day's six weeks scrolled to the selected week. Days are 66 high,
  corners of 12; the selected one amber with white words, today ringed in amber.
- **Day:** the hours, 00:00 to 23:00, a line across each, 44 high, opened at 08:00.
- **Week:** the selected day's heading as the eyebrow ("SUN 15 NOVEMBER"), and its list.
- **Month:** six weeks under the weekdays; other months' days dim, the selected one on amber at 18%,
  today ringed. Then the selected day's list.

Weeks start on Monday, on the Gregorian calendar. The tafs' scheduled work comes from tafs cloud
(`GET /v1/calendar`) in its own change. Until it's read, no day says it has nothing scheduled.

## Not yet

- **The scheduled work:** its chips in the hours (several in an hour stack), its dots on the days, a tap
  that opens the taf, and tafs cloud's line when work was left out (`more`).
- **The wide layout** a large window calls for (the month beside the calendar, Week's seven columns of
  hours, the agenda): the rule is in (`CalendarScreen.LayoutOf`); the page stays compact until next.
