using System.Collections.Concurrent;
using System.Globalization;

namespace Tafs.Core.Words;

/// <summary>
/// The culture each of tafs's languages formats dates, numbers and money with, as the iPad's <c>Words.locale</c> picks it,
/// and the language's direction.
/// </summary>
public static class Locale
{
    static readonly ConcurrentDictionary<string, CultureInfo> Cultures = new();

    /// <summary>
    /// The culture for a language the app speaks: pt is European Portuguese (pt-PT); Arabic writes the Gregorian calendar
    /// with Latin digits (the iPad's <c>ar-u-ca-gregory-nu-latn</c>; .NET formats Latin digits always); Thai keeps its
    /// Buddhist-era years, as the iPad does. A language .NET doesn't know formats as English would.
    /// </summary>
    public static CultureInfo Culture(string language) => Cultures.GetOrAdd(language, Make);

    static CultureInfo Make(string language)
    {
        CultureInfo culture;
        try { culture = (CultureInfo)CultureInfo.GetCultureInfo(language == "pt" ? "pt-PT" : language).Clone(); }
        catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo("en"); }
        if (language == "ar" && culture.DateTimeFormat.Calendar is not GregorianCalendar)
        {
            var gregorian = culture.OptionalCalendars.OfType<GregorianCalendar>().FirstOrDefault() ?? new GregorianCalendar();
            culture.DateTimeFormat.Calendar = gregorian;
        }
        return CultureInfo.ReadOnly(culture);
    }

    /// <summary>Whether a language reads right to left: Arabic and Hebrew among tafs's.</summary>
    public static bool RightToLeft(string language) => language.Split('-')[0] is "ar" or "he";

    /// <summary>A value set apart inside a line (U+2068 … U+2069), so a name or number in another direction keeps its own: the iPad's <c>Words.isolated</c>.</summary>
    public static string Isolated(string value) => "⁨" + value + "⁩";
}
