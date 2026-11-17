using System.Text.Json;
using Tafs.Core.Taf;

namespace Tafs.Core.Coach;

/// <summary>KIFF's alert target for the account: its id and the secret KIFF signs its alerts with. Never written anywhere.</summary>
public sealed record AlertTarget(string Id, string SigningSecret)
{
    public override string ToString() => $"AlertTarget {{ Id = {Id}, SigningSecret = (hidden) }}";
}

/// <summary>
/// KIFF's alerts kept with tafs cloud, at Coach's Start, as the iPad's <c>keepKIFFAlerts</c>: so a paused run hears the owner's
/// answer to a held call whether or not a phone turned alerts on. tafs cloud is asked first whether it already holds a target
/// (<c>GET /v1/alerts</c>); if not, KIFF points the account's held-call alerts at tafs cloud (<c>POST /v1/me/alert-targets</c>,
/// a signed setup call, after Start's Windows Hello) and answers with the target, which goes straight to tafs cloud
/// (<c>POST /v1/alerts</c>). A target KIFF made that tafs cloud didn't take is kept in memory for the next Start, never
/// registered twice. Best effort throughout: none of it stops Start.
/// </summary>
public static class KiffAlerts
{
    public const string CloudPath = "/v1/alerts";
    public const string TargetsPath = "/v1/me/alert-targets";

    /// <summary>Where KIFF sends the alerts: tafs cloud's own route for them.</summary>
    public static Uri Url(Uri cloud) => new(cloud, "/v1/kiff/alerts");

    public static byte[] RegisterBody(Uri url) => JsonSerializer.SerializeToUtf8Bytes(new { url = url.AbsoluteUri });

    public static byte[] KeepBody(AlertTarget target) =>
        JsonSerializer.SerializeToUtf8Bytes(new { alert_target = new { id = target.Id, signing_secret = target.SigningSecret } });

    /// <summary><c>{"alerts": true|false}</c>; none when it doesn't say.</summary>
    public static bool? Kept(JsonElement answer) =>
        Lenient.Field(answer, "alerts") is { ValueKind: JsonValueKind.True or JsonValueKind.False } a ? a.GetBoolean() : null;

    /// <summary>KIFF's answer: its id and signing secret; none without both.</summary>
    public static AlertTarget? Target(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return Lenient.Text(doc.RootElement, "id") is { Length: > 0 } id && Lenient.Text(doc.RootElement, "signing_secret") is { Length: > 0 } secret
                ? new(id, secret) : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Kept: none left to hand over. Not kept: the target to try again with next time (KIFF made it, tafs cloud didn't take it).
    /// </summary>
    /// <param name="mayAnswerHolds">KIFF's <c>holds.answer</c>: someone who can't answer a held call keeps no alerts for it.</param>
    /// <param name="pending">A target from an earlier Start that tafs cloud didn't take.</param>
    /// <param name="register">KIFF's <c>POST</c>, signed by the caller over exactly this body.</param>
    public static async Task<AlertTarget?> KeepAsync(bool mayAnswerHolds, AlertTarget? pending, Func<Task<CloudRead>> readKept,
        Func<byte[], Task<(int Status, string Body)>> register, Func<byte[], Task<CloudRead>> handOver, Uri alertsUrl, Func<bool> current)
    {
        if (!mayAnswerHolds) return pending;
        var target = pending;
        try
        {
            if (target is null)
            {
                // Kept already, or tafs cloud doesn't say: not now.
                var kept = await readKept();
                if (!current() || kept.Answer is not { } a || Kept(a) != false) return null;
                var (status, body) = await register(RegisterBody(alertsUrl));
                if (!current() || status is < 200 or > 299 || Target(body) is not { } made) return null;
                target = made;
            }
            var handed = await handOver(KeepBody(target));
            if (!current()) return null;
            return handed.Answer is null ? target : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Security.Cryptography.CryptographicException
            or System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            // A target KIFF made is kept for the next Start, whatever failed after it.
            return target;
        }
    }
}
