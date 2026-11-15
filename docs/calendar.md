# Calendar

The iPad's Calendar (`CalendarView`, `CalendarMath`), on Windows, as the reference set's `calendar.png`
(Day) and `calendar-week.png` (Week) show it on an upright iPad. What it says and counts is in
`Tafs.Core.Calendar` (`CalendarScreen`), tested in the `calendar` part. `CalendarPage` places it.

## The screen

- **"Calendar"** at the top, 17 semibold, centred.
- **The title,** in the language's order: "Sun 15 November 2026", "9 – 15 November 2026" (both months
  or years when the week crosses them), "November 2026".
- **Today,** a pill sized to its word, 15 medium with a line around; at today, quieter and inert.
- **The arrows** step a day, a week or a month (to the month's last day when it has fewer).
- **The date** on a raised pill in the person's language ("Nov 15, 2026"); it opens a calendar to pick.
- **Day / Week / Month,** the iPad's segmented control, the chosen segment lighter. It opens on Week.
- **The strip:** in Week, the seven days its title names, sharing the width; in Day, the month's six
  weeks scrolling to the selected week. Each day is 66 high with corners of 12, weekday 11 bold, number
  17 semibold; the selected one amber with white words, today ringed in amber.
- **Day:** the hours, 00:00 to 23:00, a line across each, 44 high, opened at 08:00. Then "Nothing
  scheduled on this day."
- **Week:** the selected day's heading as the eyebrow ("SUN 15 NOVEMBER"), and its list.
- **Month:** six weeks under the weekdays; other months' days dim, the selected one on amber at 18%,
  today ringed. Then the selected day's list.

Weeks start on Monday. Dates are days on the Gregorian calendar; nothing is converted between zones.

## Where the work comes from

The tafs' scheduled work comes from tafs cloud (`GET /v1/calendar`) in its own change. Until then
every day says it has nothing scheduled, and nothing shows as reading.

## Not yet

- **The scheduled work** itself: its chips in the hours, its dots on the days, a tap that opens the taf.
- **The wide layout,** the iPad's on its side: a month beside the calendar, the agenda under them. The
  reference set shows the upright iPad, and this follows it.
