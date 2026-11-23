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
    /// The culture for a language the app speaks, as the iPad's <c>Words.locale</c> picks it: the language's own (pt is
    /// CLDR's pt, as on the iPad and Linux, though tafs's Portuguese words are Portugal's). Arabic writes the Gregorian calendar with Latin digits (the iPad's <c>ar-u-ca-gregory-nu-latn</c>; .NET formats
    /// Latin digits always); Thai keeps its Buddhist-era years, as the iPad does. A language .NET doesn't know formats as
    /// English would.
    /// </summary>
    public static CultureInfo Culture(string language) => Cultures.GetOrAdd(language, Make);

    static CultureInfo Make(string language)
    {
        CultureInfo culture;
        try { culture = (CultureInfo)CultureInfo.GetCultureInfo(language).Clone(); }
        catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo("en"); }
        if (language == "ar" && culture.DateTimeFormat.Calendar is not GregorianCalendar)
        {
            var gregorian = culture.OptionalCalendars.OfType<GregorianCalendar>().FirstOrDefault() ?? new GregorianCalendar();
            culture.DateTimeFormat.Calendar = gregorian;
        }
        return CultureInfo.ReadOnly(culture);
    }

    /// <summary>
    /// Whether a language's letters join (Arabic, Urdu): monospaced text in it is drawn in the plain face, since a fixed-width face
    /// breaks its letters apart, as the iPad's <c>ScaledFont.design</c> and Android's <c>JOINED_SCRIPTS</c>.
    /// </summary>
    public static bool JoinedScript(string language) => language.Split('-')[0] is "ar" or "ur";

    /// <summary>Whether a language reads right to left: Arabic, Hebrew and Urdu among tafs's.</summary>
    public static bool RightToLeft(string language) => language.Split('-')[0] is "ar" or "he" or "ur";

    /// <summary>
    /// A value set apart inside a line (U+2068 … U+2069), so a name or number in another direction keeps its own: the iPad's
    /// <c>Words.isolated</c>. Direction marks inside the value are taken out first (<see cref="WithoutDirection"/>), so a
    /// value can't close the isolate early or turn the rest of the line.
    /// </summary>
    public static string Isolated(string value) => "⁨" + WithoutDirection(value) + "⁩";

    /// <summary>The text with U+202A–U+202E, U+2066–U+2069, U+200E, U+200F and U+061C taken out, and nothing else changed (the iPad's <c>Words.withoutDirection</c>).</summary>
    public static string WithoutDirection(string text) =>
        string.Concat(text.Where(c => c is not ((>= '‪' and <= '‮') or (>= '⁦' and <= '⁩') or '‎' or '‏' or '؜')));
}
