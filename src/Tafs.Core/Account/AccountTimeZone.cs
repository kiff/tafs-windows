using System.Text.Json;
using Tafs.Core.Kiff;
using Tafs.Core.NeedsYou;
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

    public static byte[] Body(string zone) => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["time_zone"] = zone });

    /// <summary>This PC's zone as KIFF names zones (IANA, "Europe/Paris"); none when Windows' zone has no IANA name.</summary>
    public static string? ThisPc(TimeZoneInfo local) =>
        local.HasIanaId ? local.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(local.Id, out var iana) ? iana : null;

    /// <summary>
    /// Sets it: Windows Hello first, then <paramref name="signedAt"/>'s time, then the device key's signature over exactly the
    /// request, then the PUT. Closed or failing, nothing is sent ("The time zone didn't change."). KIFF refusing says the app's
    /// line for its status. A sign-out meanwhile sends nothing more. Taken, the zone KIFF kept.
    /// </summary>
    public static async Task<(string? Zone, string? Why)> SetAsync(WordBook w, string tenant, string zone, Func<string, Task<StepConfirmed>> confirm,
        Func<byte[], Task<byte[]>> signDer, Func<string, Task<(int Status, string Body)>> put, Func<string> signedAt, Func<bool> current)
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
        try
        {
            using var doc = JsonDocument.Parse(answer);
            return (doc.RootElement.TryGetProperty("time_zone", out var z) && z.ValueKind == JsonValueKind.String ? z.GetString() : zone, null);
        }
        catch (JsonException) { return (zone, null); }
    }
}
