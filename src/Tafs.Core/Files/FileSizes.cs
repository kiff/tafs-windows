using System.Globalization;
using Tafs.Core.Words;

namespace Tafs.Core.Files;

/// <summary>
/// A file's size as each language writes it, as the iPad's <c>ByteCountFormatter</c> (file style, 1000 to a kilobyte): CLDR's
/// short byte units (from ICU 74's <c>MeasureUnit</c>, the same table as tafs-linux's <c>files::size</c>), bytes by plural
/// form, then kB, MB and GB. Bytes below a kilobyte; whole kilobytes; megabytes with one decimal and gigabytes with two, each
/// only when it isn't whole.
/// </summary>
public static class FileSizes
{
    sealed record Units((string Form, string Pattern)[] Bytes, string Kb, string Mb, string Gb);

    static Units U(string b, string kb = "{0} kB", string mb = "{0} MB", string gb = "{0} GB") => new([("other", b)], kb, mb, gb);

    static readonly Dictionary<string, Units> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = U("{0} byte"), ["de"] = U("{0} Byte"), ["fr"] = U("{0} o", "{0} ko", "{0} Mo", "{0} Go"), ["es"] = U("{0} B"),
        ["it"] = U("{0} byte"), ["pt"] = U("{0} bytes"), ["nl"] = U("{0} byte"), ["pl"] = U("{0} B"),
        ["ko"] = U("{0}byte", "{0}kB", "{0}MB", "{0}GB"), ["ja"] = U("{0} byte", "{0} KB"),
        ["et"] = new([("one", "{0} bait"), ("other", "{0} baiti")], "{0} kB", "{0} MB", "{0} GB"),
        ["lt"] = U("{0} B"), ["lv"] = U("{0} B"), ["cs"] = U("{0} B"), ["sk"] = U("{0} B"),
        ["sl"] = new([("one", "{0} bajt"), ("two", "{0} bajta"), ("few", "{0} bajti"), ("other", "{0} bajtov")], "{0} kB", "{0} MB", "{0} GB"),
        ["hu"] = U("{0} bájt"), ["ro"] = U("{0} B"), ["bg"] = U("{0} B"),
        ["hr"] = new([("one", "{0} bajt"), ("few", "{0} bajta"), ("other", "{0} bajtova")], "{0} kB", "{0} MB", "{0} GB"),
        ["tr"] = U("{0} bayt"), ["id"] = U("{0} byte"), ["vi"] = U("{0} byte"), ["th"] = U("{0} ไบต์"), ["zh"] = U("{0} byte"),
        ["ar"] = U("{0} بايت", "{0} كيلوبايت", "{0} م.ب", "{0} غ.ب"), ["sr"] = U("{0} B"), ["sq"] = U("{0} bajt"),
        ["ka"] = U("{0} ბაიტი", "{0} კბაიტი"),
        ["hy"] = new([("one", "{0} Բայթ"), ("other", "{0} Բ")], "{0} կԲ", "{0} ՄԲ", "{0} ԳԲ"),
        ["az"] = U("{0} bayt"), ["bn"] = U("{0} বাইট"), ["he"] = U("{0} בייט"), ["ca"] = U("{0} B"),
        ["mk"] = new([("one", "{0} бајт"), ("other", "{0} бајти")], "{0} kB", "{0} MB", "{0} GB"),
        ["mn"] = U("{0} байт", "{0} кБ", "{0} МБ", "{0} ГБ"), ["ne"] = U("{0} byte"), ["km"] = U("{0} byte"), ["lo"] = U("{0} byte"),
        ["tk"] = U("{0} B"), ["hi"] = U("{0} बाइट"), ["ur"] = U("{0} byte"),
    };

    /// <summary>The language's own base (<c>pt-BR</c> is <c>pt</c>, <c>zh-Hant</c> is <c>zh</c>); a language not in the table writes English's.</summary>
    static Units For(string language) =>
        Table.TryGetValue(language, out var u) || Table.TryGetValue(language.Split('-')[0], out u) ? u : Table["en"];

    public static string Words(string language, long bytes)
    {
        var u = For(language);
        var culture = Locale.Culture(language);
        bytes = Math.Max(0, bytes);
        // A count at `decimals`, rounded, its decimals shown only when they aren't zero.
        string Shown(double value, int decimals)
        {
            var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
            return rounded == Math.Floor(rounded) ? rounded.ToString("N0", culture) : rounded.ToString("N" + decimals, culture);
        }
        var (n, pattern) = bytes switch
        {
            < 1_000 => (bytes.ToString("N0", culture), u.Bytes.Length == 1 ? u.Bytes[0].Pattern : Pick(u, PluralRules.Form(language, bytes))),
            < 1_000_000 => (Shown(bytes / 1e3, 0), u.Kb),
            < 1_000_000_000 => (Shown(bytes / 1e6, 1), u.Mb),
            _ => (Shown(bytes / 1e9, 2), u.Gb),
        };
        return pattern.Replace("{0}", n, StringComparison.Ordinal);
    }

    static string Pick(Units u, string form) =>
        u.Bytes.FirstOrDefault(b => b.Form == form).Pattern ?? u.Bytes.First(b => b.Form == "other").Pattern;
}
