using System.Globalization;

namespace Tafs.Core.Words;

/// <summary>
/// "3 hours ago", as Apple's <c>RelativeDateTimeFormatter</c> writes a past time in full in each shipped language, which
/// .NET has no formatter for. The unit is the largest one with at least one: seconds, minutes, hours, days, weeks, months
/// (30 days) or years (365 days), counted from the time elapsed. A time to come, or a language without a table here, says
/// it in English.
/// </summary>
public static class RelativeTime
{
    enum Unit { Second, Minute, Hour, Day, Week, Month, Year }

    /// <summary>Each language: how a count and its unit are put ("{0} ago"), and each unit's forms by plural category.</summary>
    static readonly Dictionary<string, (string Pattern, Dictionary<Unit, Dictionary<string, string>> Units)> Languages = new()
    {
        ["en"] = ("{0} ago", Forms(("second", "seconds"), ("minute", "minutes"), ("hour", "hours"), ("day", "days"), ("week", "weeks"), ("month", "months"), ("year", "years"))),
        ["de"] = ("vor {0}", Forms(("Sekunde", "Sekunden"), ("Minute", "Minuten"), ("Stunde", "Stunden"), ("Tag", "Tagen"), ("Woche", "Wochen"), ("Monat", "Monaten"), ("Jahr", "Jahren"))),
        ["fr"] = ("il y a {0}", Forms(("seconde", "secondes"), ("minute", "minutes"), ("heure", "heures"), ("jour", "jours"), ("semaine", "semaines"), ("mois", "mois"), ("an", "ans"))),
        ["es"] = ("hace {0}", Forms(("segundo", "segundos"), ("minuto", "minutos"), ("hora", "horas"), ("día", "días"), ("semana", "semanas"), ("mes", "meses"), ("año", "años"))),
        ["it"] = ("{0} fa", Forms(("secondo", "secondi"), ("minuto", "minuti"), ("ora", "ore"), ("giorno", "giorni"), ("settimana", "settimane"), ("mese", "mesi"), ("anno", "anni"))),
        ["pt"] = ("há {0}", Forms(("segundo", "segundos"), ("minuto", "minutos"), ("hora", "horas"), ("dia", "dias"), ("semana", "semanas"), ("mês", "meses"), ("ano", "anos"))),
        ["nl"] = ("{0} geleden", Forms(("seconde", "seconden"), ("minuut", "minuten"), ("uur", "uur"), ("dag", "dagen"), ("week", "weken"), ("maand", "maanden"), ("jaar", "jaar"))),
        ["pl"] = ("{0} temu", new()
        {
            [Unit.Second] = Polish("sekundę", "sekundy", "sekund"),
            [Unit.Minute] = Polish("minutę", "minuty", "minut"),
            [Unit.Hour] = Polish("godzinę", "godziny", "godzin"),
            [Unit.Day] = Polish("dzień", "dni", "dni"),
            [Unit.Week] = Polish("tydzień", "tygodnie", "tygodni"),
            [Unit.Month] = Polish("miesiąc", "miesiące", "miesięcy"),
            [Unit.Year] = Polish("rok", "lata", "lat"),
        }),
    };

    static Dictionary<Unit, Dictionary<string, string>> Forms(params (string One, string Other)[] units) =>
        units.Select((u, i) => ((Unit)i, new Dictionary<string, string> { ["one"] = u.One, ["other"] = u.Other })).ToDictionary(x => x.Item1, x => x.Item2);

    static Dictionary<string, string> Polish(string one, string few, string many) => new() { ["one"] = one, ["few"] = few, ["many"] = many, ["other"] = many };

    /// <summary><paramref name="then"/> before <paramref name="now"/>, in full: "3 hours ago", "il y a 3 heures", "3 godziny temu".</summary>
    public static string Ago(string language, DateTimeOffset then, DateTimeOffset now)
    {
        var elapsed = now - then;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        var (unit, count) = elapsed.TotalSeconds switch
        {
            < 60 => (Unit.Second, (long)elapsed.TotalSeconds),
            < 3600 => (Unit.Minute, (long)elapsed.TotalMinutes),
            < 86400 => (Unit.Hour, (long)elapsed.TotalHours),
            < 7 * 86400 => (Unit.Day, (long)elapsed.TotalDays),
            < 30 * 86400 => (Unit.Week, (long)(elapsed.TotalDays / 7)),
            < 365 * 86400 => (Unit.Month, (long)(elapsed.TotalDays / 30)),
            _ => (Unit.Year, (long)(elapsed.TotalDays / 365)),
        };
        var code = language.Split('-')[0];
        var (pattern, units) = Languages.TryGetValue(code, out var table) ? table : Languages["en"];
        var forms = units[unit];
        var form = forms.TryGetValue(PluralRules.Form(Languages.ContainsKey(code) ? code : "en", count), out var f) ? f : forms["other"];
        return string.Format(CultureInfo.InvariantCulture, pattern, $"{count.ToString(CultureInfo.InvariantCulture)} {form}");
    }
}
