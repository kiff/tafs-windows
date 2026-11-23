using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Tafs.Core.Taf;

namespace Tafs.Core.Kiff;

/// <summary>One picture kept for an operation's Chrome step, as planque lists it: no bytes.</summary>
public sealed record ShotRef(string Step, DateTimeOffset? TakenAt);

/// <summary>A step's picture, checked: the step, when it was taken, and its WebP bytes as kept.</summary>
public sealed record ChromeShot(string Step, DateTimeOffset TakenAt, byte[] Image);

/// <summary>What reading a step's picture came to: shown, none kept (planque's 404), or a read or picture that didn't check out.</summary>
public enum ShotRead { Shown, None, Failed }

/// <summary>
/// The page as a paired Chrome showed it just before a taf's step acted (docs/chrome.md, "Screenshots of a run's acts"), as the
/// iPad's <c>ChromeShotView</c> and <c>PlanqueClient.chromeScreenshots</c>: a Record line's receipt lists the pictures kept for its
/// operation (KIFF's <c>proposal_id</c>), and each opens its step's. Both reads are planque's safe routes, signed by this PC's
/// device key with no session token; planque keeps none while its switch is off.
/// A picture is shown only if it checks out: WebP, at most 120 KB, its SHA-256 the one planque kept, the step asked for, and a
/// time it was taken. Anything else is no picture, never shown.
/// </summary>
public static class ChromeShots
{
    /// <summary>The most a picture may weigh: planque keeps none over 120 KB.</summary>
    public const int Largest = 120 * 1024;

    /// <summary><c>GET /v1/accounts/{t}/chromes/screenshots?op={operation}</c>: the pictures kept for one operation's Chrome steps.</summary>
    public static string ListPath(string tenant, string operation) =>
        $"{PlanqueChromes.ListPath(tenant)}/screenshots?op={Uri.EscapeDataString(operation)}";

    /// <summary><c>GET /v1/accounts/{t}/chromes/screenshots/{step}</c>: one step's picture.</summary>
    public static string ShotPath(string tenant, string step) => $"{PlanqueChromes.ListPath(tenant)}/screenshots/{CloudRead.PathPart(step)}";

    /// <summary>
    /// planque's list for an operation, oldest first: none when there are none, when its switch is off (404), or when the answer
    /// can't be read. A step without its id is left out.
    /// </summary>
    public static IReadOnlyList<ShotRef> ReadList(int status, string body)
    {
        if (status is < 200 or > 299) return [];
        try
        {
            var a = JsonDocument.Parse(body).RootElement;
            if (a.ValueKind != JsonValueKind.Object || Lenient.Field(a, "shots") is not { ValueKind: JsonValueKind.Array } list) return [];
            return list.EnumerateArray()
                .Where(s => s.ValueKind == JsonValueKind.Object && Lenient.Text(s, "step")?.Trim() is { Length: > 0 })
                .Select(s => new ShotRef(Lenient.Text(s, "step")!.Trim(), Time(Lenient.Text(s, "taken_at"))))
                .ToList();
        }
        catch (JsonException) { return []; }
    }

    /// <summary>planque's answer to a step's picture: shown when it checks out; none kept on a 404; failed for anything else.</summary>
    public static (ShotRead Read, ChromeShot? Shot) Read(int status, string body, string step)
    {
        if (status == 404) return (ShotRead.None, null);
        if (status is < 200 or > 299) return (ShotRead.Failed, null);
        return Checked(body) is { } shot && shot.Step == step ? (ShotRead.Shown, shot) : (ShotRead.Failed, null);
    }

    /// <summary>
    /// The picture, or none when it isn't one to show: not the shape, not base64url without padding, too big, not WebP, bytes
    /// whose SHA-256 isn't planque's, or no time it was taken.
    /// </summary>
    static ChromeShot? Checked(string body)
    {
        JsonElement v;
        try { v = JsonDocument.Parse(body).RootElement; }
        catch (JsonException) { return null; }
        if (v.ValueKind != JsonValueKind.Object) return null;
        if (Lenient.Text(v, "step") is not { } step || Lenient.Text(v, "sha256") is not { } sha || Lenient.Text(v, "image") is not { } encoded) return null;
        if (Base64Url(encoded) is not { } image || image.Length < 12 || image.Length > Largest) return null;
        if (!(image[0] == 'R' && image[1] == 'I' && image[2] == 'F' && image[3] == 'F' && image[8] == 'W' && image[9] == 'E' && image[10] == 'B' && image[11] == 'P'))
            return null;
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(image)), sha, StringComparison.OrdinalIgnoreCase)) return null;
        return Time(Lenient.Text(v, "taken_at")) is { } taken ? new ChromeShot(step, taken, image) : null;
    }

    /// <summary>base64url as planque writes it, with no padding; anything else (padding, other characters) is none.</summary>
    static byte[]? Base64Url(string text)
    {
        if (text.Length == 0 || text.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) return null;
        var standard = text.Replace('-', '+').Replace('_', '/');
        standard += (standard.Length % 4) switch { 2 => "==", 3 => "=", 0 => "", _ => "!" };
        try { return Convert.FromBase64String(standard); }
        catch (FormatException) { return null; }
    }

    /// <summary>An RFC 3339 time with its offset; none for anything else.</summary>
    static DateTimeOffset? Time(string? text) =>
        text is not null && DateTimeOffset.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var t) ? t : null;
}
