# Calendar

The iPad's Calendar (`CalendarView`, `CalendarMath`), on Windows, as the reference set's `calendar.png`
(Day) and `calendar-week.png` (Week) show it on an upright iPad. What it says and counts is in
`Tafs.Core.Calendar` (`CalendarScreen`), tested in the `calendar` part. `CalendarPage` places it.

## The screen

- **"Calendar"** at the top, 17 semibold, centred.
- **The title,** in the order the language writes a date: "Sun 15 November 2026" (Day), "9 – 15 November
  2026" (Week, or with both months or years when the week crosses them), "November 2026" (Month).
- **Today,** a pill sized to its word in every language: 15 medium, with a line around. At today it's
  quieter (the soft colour, a fainter line) and does nothing.
- **The arrows** step a day, a week or a month, in the amber. A month step lands on the month's last day
  when it has fewer. They're named "Previous day", "Next week"…
- **The date** on a raised pill, in the person's language ("Nov 15, 2026", "15 nov. 2026"). It opens
  Windows' calendar to pick a day.
- **Day / Week / Month,** the iPad's segmented control: the chosen segment lighter than its track. It
  opens on Week.
- **The strip:**
  - in Week, exactly the seven days its title names, sharing the width;
  - in Day, the month's six weeks in a row that scrolls, the selected day's week first;
  - each day 66 high, corners of 12, its weekday 11 bold and number 17 semibold; the selected one amber
    with white words, today ringed in amber.
- **Day:** the hours, 00:00 to 23:00, a line across each, 44 high, opened at 08:00. Then "Nothing
  scheduled on this day."
- **Week:** the selected day's heading as the eyebrow ("SUN 15 NOVEMBER"), and its list.
- **Month:** the weekdays, then six weeks of days; days of other months dim, the selected day on amber at
  18%, today ringed. Then the selected day's list.

Weeks start on Monday. Dates are days on the Gregorian calendar; nothing is converted between zones.

## Where the work comes from

The tafs' scheduled work comes from tafs cloud (`GET /v1/calendar`) in its own change. Until then
every day says it has nothing scheduled, and nothing shows as reading.

## Not yet

- **The scheduled work** itself: its chips in the hours, its dots on the days, a tap that opens the taf.
- **The wide layout,** the iPad's on its side: a month beside the calendar, the agenda under them. The
  reference set shows the upright iPad, and this follows it.
