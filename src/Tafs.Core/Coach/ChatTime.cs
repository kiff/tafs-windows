using System.Globalization;
using System.Text.RegularExpressions;
using Tafs.Core.Words;

namespace Tafs.Core.Coach;

/// <summary>
/// When each chat message was sent (kiff/tafs docs/chat.md, "When each message was sent"), as the iPhone's and Android's
/// <c>ChatTime</c>: a small time under each message, and a header where a day starts: Today, Yesterday, then the date with its
/// weekday in the language's own order ("Wed, Nov 18"), with the year when it isn't this one. Times follow this PC's 12- or
/// 24-hour setting, in the app's language; days and times are the account's zone. It replaces the time drawn between parts an
/// hour apart.
/// </summary>
public static partial class ChatTime
{
    /// <summary>Whether this PC's clock reads 24 hours (its short time has <c>H</c>), whatever language the app is in.</summary>
    public static bool Uses24Hour(CultureInfo device) => device.DateTimeFormat.ShortTimePattern.Contains('H');

    /// <summary>The time under a message: "14:05", or "2:05 PM" on a 12-hour PC, in the app's language.</summary>
    public static string Time(DateTimeOffset at, TimeZoneInfo zone, WordBook w, bool twentyFour)
    {
        var culture = Locale.Culture(w.Language);
        var local = TimeZoneInfo.ConvertTime(at, zone);
        return twentyFour || culture.DateTimeFormat.AMDesignator.Length == 0
            ? local.ToString("HH:mm", culture)
            : local.ToString("h:mm tt", culture);
    }

    /// <summary>The header where a day starts: Today, Yesterday, else the date with its weekday, and its year when it isn't this one.</summary>
    public static string Day(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone, WordBook w)
    {
        var day = TimeZoneInfo.ConvertTime(at, zone).Date;
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        if (day == today) return w.Get("calendar.today");
        if (day == today.AddDays(-1)) return w.Get("chat.day.yesterday");
        var culture = Locale.Culture(w.Language);
        return day.ToString(DayPattern(culture, withYear: day.Year != today.Year), culture);
    }

    /// <summary>
    /// The date with its weekday, short, in the language's own order: its long date with the weekday and month abbreviated,
    /// and its year taken out when it's this one ("ddd, MMM d", "ddd d MMM", "M'月'd'日'ddd").
    /// </summary>
    public static string DayPattern(CultureInfo culture, bool withYear)
    {
        var pattern = culture.DateTimeFormat.LongDatePattern.Replace("dddd", "ddd").Replace("MMMM", "MMM");
        if (withYear) return pattern;
        // The year with what's written beside it ("yyyy'年'", ", yyyy", " yyyy г."), then any separator left at an end.
        pattern = Year().Replace(pattern, "");
        return Ends().Replace(pattern, "").Trim();
    }

    /// <summary>Whether the message at <paramref name="i"/> starts a day: the first one, or the first on a later day than the one before.</summary>
    public static bool StartsDay(IReadOnlyList<ThreadItem> items, int i, TimeZoneInfo zone) =>
        i == 0 || TimeZoneInfo.ConvertTime(items[i].At, zone).Date != TimeZoneInfo.ConvertTime(items[i - 1].At, zone).Date;

    [GeneratedRegex(@"(\s*'[^']*'\s*)?[\s,./-]*y+(\s*'[^']*')?(\s*г\.)?[年년]?")]
    private static partial Regex Year();

    [GeneratedRegex(@"^[\s,./-]+|[\s,/-]+$")]
    private static partial Regex Ends();
}
