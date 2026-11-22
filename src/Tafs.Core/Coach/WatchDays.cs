using System.Globalization;
using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Coach;

/// <summary>A day the person asked from their watch (<c>yyyy-MM-dd</c>, in the account's zone) and how many requests.</summary>
public sealed record WatchDay(string Date, int Count);

/// <summary>The days, newest first, and which is today in the account's zone (tafs cloud's <c>today</c>).</summary>
public sealed record WatchDayList(IReadOnlyList<WatchDay> Days, string? Today);

/// <summary>
/// One request said from a watch that day (<c>GET /v1/watch/days/{date}</c>): what was said and when, the taf that took it,
/// its state, what came of it (tafs cloud's line, else its words for the state), and the request it replies to.
/// </summary>
public sealed record WatchTurn(string Id, string Said, string SaidAt, string? TafName, string State, string? Line, string Words, string? ReplyTo)
{
    /// <summary>What came of it, as the watch shows it: tafs cloud's line when there is one, else its words for the state.</summary>
    public string Outcome => Line ?? Words;
}

/// <summary>
/// W11, "From your watch" (docs/watch-app.md), as the iPad's <c>TafsCloud+WatchDays</c> and <c>WatchDaysModel</c>: in Coach's
/// Previous conversations, the watch's conversations, one a day, newest first. Only the person's own, read only.
/// </summary>
public static class WatchDays
{
    public const string ListPath = "/v1/watch/days";
    public static string DayPath(string date) => ListPath + "/" + CloudRead.PathPart(date);

    /// <summary>The days and today; a day this app can't read is left out, never the list.</summary>
    public static WatchDayList ReadList(JsonElement a)
    {
        var days = Lenient.Array(a, "days")
            .Where(d => d.ValueKind == JsonValueKind.Object && Lenient.Text(d, "date") is { Length: > 0 } && Lenient.Int(d, "count") is not null)
            .Select(d => new WatchDay(Lenient.Text(d, "date")!, Lenient.Int(d, "count")!.Value)).ToList();
        return new(days, a.ValueKind == JsonValueKind.Object ? Lenient.Text(a, "today") : null);
    }

    /// <summary>
    /// The person's watch days: none when tafs cloud's switch is off (404 <c>watch_off</c>), never an error; null with why when
    /// it couldn't be read.
    /// </summary>
    public static async Task<(WatchDayList? Days, string? Why)> LoadAsync(Func<string, Task<CloudRead>> read)
    {
        var answer = await read(ListPath);
        if (answer.Status == 404) return (new([], null), null);
        return answer.Answer is { } a ? (ReadList(a), null) : (null, answer.Why);
    }

    /// <summary>
    /// One day's turns, oldest first as tafs cloud sends them; one it can't read is left out, and so is a message to a taf
    /// (<c>kind: "taf"</c>): it's in that taf's own chat, not Coach's.
    /// </summary>
    public static IReadOnlyList<WatchTurn> ReadDay(JsonElement a) =>
        Lenient.Array(a, "turns")
            .Where(t => t.ValueKind == JsonValueKind.Object && Lenient.Text(t, "id") is not null && Lenient.Text(t, "said") is not null && Lenient.Text(t, "kind") != "taf")
            .Select(t => new WatchTurn(Lenient.Text(t, "id")!, Lenient.Text(t, "said")!, Lenient.Text(t, "said_at") ?? "",
                Lenient.Field(t, "taf") is { ValueKind: JsonValueKind.Object } taf ? Lenient.Text(taf, "name") : null,
                Lenient.Text(t, "state") ?? "", Lenient.Text(t, "line") is { Length: > 0 } line ? line : null, Lenient.Text(t, "words") ?? "",
                Lenient.Text(t, "reply_to")))
            .ToList();

    /// <summary>A day's title: Today on tafs cloud's today (else this PC's, in the account's zone), else its date written out.</summary>
    public static string Title(WordBook w, string date, string? today, TimeZoneInfo zone, DateTimeOffset now)
    {
        if (date == (today ?? TimeZoneInfo.ConvertTime(now, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))) return w.Get("calendar.today");
        return DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dddd d MMMM", Locale.Culture(w.Language)) : date;
    }

    /// <summary>When a turn was said, short, in the account's zone; none for one tafs cloud didn't give.</summary>
    public static string? Time(string saidAt, TimeZoneInfo zone) =>
        DateTimeOffset.TryParse(saidAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? TimeZoneInfo.ConvertTime(at, zone).ToString("HH:mm", CultureInfo.InvariantCulture) : null;
}
