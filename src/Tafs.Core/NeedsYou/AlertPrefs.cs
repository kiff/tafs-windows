using System.Text.Json;
using Tafs.Core.Words;

namespace Tafs.Core.NeedsYou;

/// <summary>
/// What the person chose for this PC's alerts, as the iPad's <c>alertsEnabledKey</c> and <c>alertSound</c>: alerts on or
/// off for each account (off until turned on, so one account never inherits another's choice), and Sound for the whole PC
/// (on until turned off). Kept beside the session as one small file.
/// </summary>
public sealed record AlertPrefs(IReadOnlyDictionary<string, bool> On, bool Sound)
{
    public static readonly AlertPrefs Default = new(new Dictionary<string, bool>(), true);

    /// <summary>Alerts are on for the account only once the person turned them on there.</summary>
    public bool IsOn(string tenant) => On.TryGetValue(tenant, out var on) && on;

    public AlertPrefs With(string tenant, bool on) => this with { On = new Dictionary<string, bool>(On) { [tenant] = on } };

    /// <summary>The kept choices; anything unreadable is the defaults: off everywhere, Sound on.</summary>
    public static AlertPrefs Read(byte[]? data)
    {
        if (data is null) return Default;
        try
        {
            var r = JsonDocument.Parse(data).RootElement;
            var on = r.TryGetProperty("on", out var o) && o.ValueKind == JsonValueKind.Object
                ? o.EnumerateObject().Where(p => p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False).ToDictionary(p => p.Name, p => p.Value.GetBoolean())
                : new Dictionary<string, bool>();
            var sound = !r.TryGetProperty("sound", out var s) || s.ValueKind != JsonValueKind.False;
            return new(on, sound);
        }
        catch (JsonException) { return Default; }
    }

    public byte[] Write() => JsonSerializer.SerializeToUtf8Bytes(new { on = On, sound = Sound });
}

/// <summary>An alert as Account › Alerts shows it (the iPad's <c>LockScreenAlert</c>): its title, its line, and whether it sounds.</summary>
public sealed record AlertPreview(string Title, string Line, bool Loud)
{
    /// <summary>What Narrator reads: "With sound: Chargebacks needs you. Dispute #4825 · €640 is over its limit".</summary>
    public string Spoken(WordBook w) => $"{w.Get(Loud ? "alert.with_sound" : "alert.silent")}: {Title}. {Line}";
}

/// <summary>Account › Alerts, as the iPad's <c>NotificationSettingsView</c>, for this PC's own alerts.</summary>
public static class AlertsScreen
{
    /// <summary>What an alert looks like: a hold, with sound, and an answer by email, silent.</summary>
    public static IReadOnlyList<AlertPreview> Previews(WordBook w) =>
    [
        new(w.Get("settings.chargebacks_needs_you"), w.Get("settings.dispute_over_limit"), true),
        new(w.Get("settings.refunds_answered_maria"), w.Get("alert.by_email"), false),
    ];

    /// <summary>"Alerts on this Windows PC".</summary>
    public static string OnThisDevice(WordBook w) => w.Get("alert.on_this_device", ("device", w.Get("device.windows")));

    /// <summary>
    /// Whether Needs you offers alerts (the iPad's <c>AlertsCard</c>, as Windows has it): signed in with KIFF on a PC holding the
    /// session's device key, for someone who answers the account's holds, while this account's alerts are off here (not chosen,
    /// or Windows doesn't allow tafs's notifications). An alert comes only while tafs runs, so the card never says "even with
    /// tafs closed".
    /// </summary>
    public static bool Offers(bool signedIn, bool hasDeviceKey, bool mayAnswerHolds, bool alertsOn) =>
        signedIn && hasDeviceKey && mayAnswerHolds && !alertsOn;
}
