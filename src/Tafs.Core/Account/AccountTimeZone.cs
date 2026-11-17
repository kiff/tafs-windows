using System.Text.Json;
using Tafs.Core.Kiff;
using Tafs.Core.NeedsYou;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Account;

/// <summary>
/// The account's time zone set from this PC, as the iPad's <c>setAccountTimeZone</c>: the person confirms with Windows Hello
/// ("Set your account's time zone to Europe/Paris", the iPad's Face ID prompt), then the session's device key signs KIFF's
/// <c>PUT /v1/me/time-zone</c> as a signed setup call (<c>KIFF-Device-Signature</c>). It ends a Card's day, and every time the
/// owner reads is in it.
/// </summary>
public static class AccountTimeZone
{
    public const string Path = "/v1/me/time-zone";

    /// <summary>
    /// tafs cloud's route that moves every recurring schedule of the account's tafs to the zone KIFF now has, once KIFF
    /// confirmed it, as the iPad's <c>syncTimeZone</c>: no body, no zone from the app.
    /// </summary>
    public const string SchedulesPath = "/v1/account/time-zone";

    /// <summary>tafs cloud's list of zones to choose from, searched there: <c>GET /v1/time-zones?q=</c>.</summary>
    public static string ListPath(string? query) =>
        query?.Trim() is { Length: > 0 } q ? $"/v1/time-zones?q={CloudRead.QueryPart(q)}" : "/v1/time-zones";

    /// <summary>The list, read leniently: a zone without its id is left out; one without a title is named by its id.</summary>
    public static IReadOnlyList<(string Id, string Title)>? Zones(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object || !answer.TryGetProperty("zones", out var zones) || zones.ValueKind != JsonValueKind.Array) return null;
        var list = new List<(string, string)>();
        foreach (var z in zones.EnumerateArray())
        {
            if (z.ValueKind != JsonValueKind.Object || !z.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                || id.GetString()!.Trim() is not { Length: > 0 } zone) continue;
            var title = z.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String && t.GetString()!.Trim().Length > 0 ? t.GetString()! : zone;
            list.Add((zone, title));
        }
        return list;
    }

    public static byte[] Body(string zone) => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["time_zone"] = zone });

    /// <summary>This PC's zone as KIFF names zones (IANA, "Europe/Paris"); none when Windows' zone has no IANA name.</summary>
    public static string? ThisPc(TimeZoneInfo local) =>
        local.HasIanaId ? local.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(local.Id, out var iana) ? iana : null;

    /// <summary>
    /// Sets it: Windows Hello first, then <paramref name="signedAt"/>'s time, then the device key's signature over exactly the
    /// request, then the PUT. Closed or failing, nothing is sent ("The time zone didn't change."). KIFF refusing says the app's
    /// line for its status. A sign-out meanwhile sends nothing more. Taken, the zone KIFF kept; then tafs cloud moves the
    /// tafs' schedules to it (<paramref name="syncSchedules"/>, <see cref="SchedulesPath"/>), and if it can't yet, the zone is
    /// still changed and why says so.
    /// </summary>
    public static async Task<(string? Zone, string? Why)> SetAsync(WordBook w, string tenant, string zone, Func<string, Task<StepConfirmed>> confirm,
        Func<byte[], Task<byte[]>> signDer, Func<string, Task<(int Status, string Body)>> put, Func<string> signedAt, Func<bool> current,
        Func<Task<CloudRead>>? syncSchedules = null)
    {
        string NotChanged(string why) => w.Get("settings.time_zone.not_changed", ("why", why)).Trim();
        var confirmed = await confirm(w.Get("settings.time_zone.prompt", ("zone", zone)));
        if (!current()) return (null, null);
        if (confirmed != StepConfirmed.Yes) return (null, NotChanged(confirmed == StepConfirmed.Failed ? w.Get("common.not_sent_unconfirmed") : ""));

        var body = Body(zone);
        var at = signedAt();
        string header;
        try { header = KiffSigning.SignatureHeaderV1(at, await signDer(KiffSigning.RequestV1(tenant, "PUT", Path, body, at))); }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or System.Runtime.InteropServices.COMException
            or InvalidOperationException or UnauthorizedAccessException)
        {
            return (null, NotChanged(w.Get("common.not_sent_unconfirmed")));
        }
        if (!current()) return (null, null);

        int status;
        string answer;
        try { (status, answer) = await put(header); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { (status, answer) = (0, ""); }
        if (!current()) return (null, null);
        if (status is < 200 or > 299) return (null, NotChanged(KiffErrors.Words(w, status)));
        string kept;
        try
        {
            using var doc = JsonDocument.Parse(answer);
            kept = doc.RootElement.TryGetProperty("time_zone", out var z) && z.ValueKind == JsonValueKind.String ? z.GetString()! : zone;
        }
        catch (JsonException) { kept = zone; }
        if (syncSchedules is null) return (kept, null);
        // KIFF has it: tafs cloud moves the tafs' schedules to it, as the iPad's syncTimeZone.
        var synced = await syncSchedules();
        if (!current()) return (kept, null);
        return synced.Answer is not null ? (kept, null)
            : (kept, w.Get("settings.account_zone_changed_schedules_could", ("why", synced.Why ?? w.Get("error.offline"))));
    }
}
