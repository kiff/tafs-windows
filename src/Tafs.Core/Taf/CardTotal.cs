using System.Collections.Concurrent;
using System.Globalization;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>
/// An amount as KIFF gives it (the iPad's Money): an integer, its unit (a currency code such as "EUR", another unit, or
/// none) and its decimal places. Without a unit it's a bare number, scale 0: the app never guesses a currency or a scale.
/// </summary>
public sealed record KiffAmount
{
    public KiffAmount(long minor, string? unit, int scale)
    {
        Minor = minor;
        Unit = string.IsNullOrWhiteSpace(unit) ? null : unit;
        Scale = Unit is null ? 0 : Math.Max(0, scale);
    }

    public long Minor { get; }
    public string? Unit { get; }
    public int Scale { get; }

    public static KiffAmount Euros(decimal euros) => new((long)decimal.Round(euros * 100), "EUR", 2);

    /// <summary>The same unit and scale: amounts that can be taken from one another.</summary>
    public bool SameKind(KiffAmount other) => Unit == other.Unit && Scale == other.Scale;

    /// <summary>
    /// As shown in the app's language, never the PC's region: "€2,000" (a currency keeps its own symbol and isn't
    /// converted), "1,500 credits" for another unit, "30,000" for none. Decimals only when it has some.
    /// </summary>
    public string Figure(WordBook w)
    {
        var culture = CultureInfo.GetCultureInfo(w.Language == "pt" ? "pt-PT" : w.Language);
        var value = Minor / (decimal)Math.Pow(10, Scale);
        var places = value == decimal.Truncate(value) ? 0 : Scale;
        if (Unit is null) return value.ToString("N" + places, culture);
        if (Symbol(Unit) is { } symbol)
        {
            var format = (NumberFormatInfo)culture.NumberFormat.Clone();
            format.CurrencySymbol = symbol;
            return value.ToString("C" + places, format);
        }
        return $"{value.ToString("N" + places, culture)} {Unit}";
    }

    /// <summary>As shown on its own: the figure, or for a bare number, saying it has none ("30,000 (no currency)").</summary>
    public string Formatted(WordBook w) => Unit is null ? w.Get("card.no_currency", ("amount", Figure(w))) : Figure(w);

    static readonly ConcurrentDictionary<string, string?> Symbols = new();

    /// <summary>A currency code's own symbol ("EUR" is "€"), or none for a unit that isn't a currency.</summary>
    static string? Symbol(string code) => Symbols.GetOrAdd(code.ToUpperInvariant(), c =>
        CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(ci => { try { return new RegionInfo(ci.Name); } catch (ArgumentException) { return null; } })
            .FirstOrDefault(r => r?.ISOCurrencySymbol == c)?.CurrencySymbol);
}

/// <summary>
/// A Card's spending window: KIFF's own name for it and its words, as the iPad's Window. <paramref name="Label"/> ends a
/// limit ("a day"); <paramref name="Current"/> is its use so far ("today"). A window this app doesn't know reads as sent.
/// </summary>
public sealed record CardWindow(string Raw, string Label, string Current)
{
    /// <summary>Ends an amount's total: "in total a day" for a day's, so a bare number reads apart from a count.</summary>
    public string SumLabel(WordBook w) => Raw == "calendar_day" ? w.Get("card.window.in_total_window", ("window", Label)) : Label;

    static readonly Dictionary<string, (string Each, string Now)> Known = new()
    {
        ["calendar_day"] = ("card.window.a_day", "card.window.today"),
        ["rolling_24h"] = ("card.window.any_day", "card.window.last_day"),
        ["rolling_7d"] = ("card.window.any_week", "card.window.last_week"),
        ["rolling_1h"] = ("card.window.any_hour", "card.window.last_hour"),
        ["lifetime"] = ("card.window.in_total", "card.window.so_far"),
    };

    /// <summary>KIFF's window, in tafs cloud's words when it sent them, else the app's for KIFF's five.</summary>
    public static CardWindow Of(WordBook w, string raw, string? each = null, string? now = null) =>
        new(raw,
            each ?? (Known.TryGetValue(raw, out var k) ? w.Get(k.Each) : raw),
            now ?? (Known.TryGetValue(raw, out var n) ? w.Get(n.Now) : raw));
}

/// <summary>A Card's money total for its window: its limit, and what it has used, null when KIFF couldn't read it (never zero).</summary>
public sealed record CardTotal(KiffAmount Limit, KiffAmount? Used, CardWindow Window);

/// <summary>A Card's count of calls for its window: its limit, and how many it has made, null when KIFF couldn't read it.</summary>
public sealed record CallsTotal(int Limit, int? Used, CardWindow Window);
