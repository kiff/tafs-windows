using System.Globalization;

namespace Tafs.Core.Words;

/// <summary>
/// "3 hours ago", as Apple's <c>RelativeDateTimeFormatter</c> writes a past time in full in each language, which .NET has
/// no formatter for: CLDR's patterns (<see cref="Cldr.Past"/>, generated). The unit is the largest one with at least one:
/// seconds, minutes, hours, days, weeks, months (30 days) or years (365 days), counted from the time elapsed. A time to
/// come counts as now; a language CLDR's table lacks says it in English.
/// </summary>
public static class RelativeTime
{
    /// <summary><paramref name="then"/> before <paramref name="now"/>, in full: "3 hours ago", "il y a 3 heures", "3 godziny temu".</summary>
    public static string Ago(string language, DateTimeOffset then, DateTimeOffset now)
    {
        var elapsed = now - then;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        var (unit, count) = elapsed.TotalSeconds switch
        {
            < 60 => ("second", (long)elapsed.TotalSeconds),
            < 3600 => ("minute", (long)elapsed.TotalMinutes),
            < 86400 => ("hour", (long)elapsed.TotalHours),
            < 7 * 86400 => ("day", (long)elapsed.TotalDays),
            < 30 * 86400 => ("week", (long)(elapsed.TotalDays / 7)),
            < 365 * 86400 => ("month", (long)(elapsed.TotalDays / 30)),
            _ => ("year", (long)(elapsed.TotalDays / 365)),
        };
        var code = Cldr.Past.ContainsKey(language) ? language : "en";
        var forms = Cldr.Past[code][unit];
        var pattern = forms.TryGetValue(PluralRules.Form(code, count), out var p) ? p : forms["other"];
        return pattern.Replace("{0}", count.ToString(Locale.Culture(code)));
    }
}
