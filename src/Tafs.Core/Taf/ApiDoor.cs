using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>An <c>api</c> door's last request: tafs cloud's status, when, and its words (none until it sends them).</summary>
public sealed record ApiLastRequest(string Status, DateTimeOffset? At, string? Words);

/// <summary>A key as tafs cloud makes it: shown in this one answer only, never again.</summary>
public sealed record IssuedKey(string Id, string Key, string Last4, DateTimeOffset? MadeAt);

/// <summary>
/// A door "From another application" (<c>api</c>, docs/doors.md), as the iPad's <c>APIDoorText</c> and <c>TafsCloud+APIDoors</c>:
/// a company's own software hands the taf work with a key. Its keys are made and revoked, and the door opened again or
/// removed, only signed by this PC's approval (as a taf's removal is) over the method, path and these exact bytes; pausing
/// isn't signed: stopping is always safe. Drawn wherever tafs cloud sends one (<c>TAFS_API_DOORS</c> alone decides).
/// </summary>
public static class ApiDoor
{
    /// <summary>A door holds at most two live keys, so one can be replaced without stopping the other.</summary>
    public const int MaxKeys = 2;

    /// <summary>The longest key name the field takes (tafs cloud takes 80; 60, as the iPad and Android).</summary>
    public const int MaxKeyName = 60;

    static readonly JsonSerializerOptions Plain = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>New key's body, its name: the exact bytes signed and sent, as the iPad's <c>signedBody</c> (no slash escaped).</summary>
    public static byte[] NewKeyBody(string name) => Encoding.UTF8.GetBytes("{\"name\":" + JsonSerializer.Serialize(name, Plain) + "}");

    /// <summary>Opening a paused door again: the one change to a door's open switch that's signed.</summary>
    public static byte[] ReopenBody() => "{\"open\":true}"u8.ToArray();

    /// <summary>A signed removal's or revocation's body: none.</summary>
    public static byte[] NoBody() => [];

    /// <summary>The key tafs cloud made, from its answer; none when it isn't one (a key is never guessed at).</summary>
    public static IssuedKey? ReadIssued(JsonElement a) =>
        a.ValueKind == JsonValueKind.Object && Lenient.Text(a, "id") is { Length: > 0 } id && Lenient.Text(a, "key") is { Length: > 0 } key
            ? new(id, key, Lenient.Text(a, "last4") ?? "", Lenient.At(a, "made_at")) : null;

    /// <summary>A new key's name: what the person typed, trimmed and at most 60, else the door's own name.</summary>
    public static string KeyName(string typed, Door door)
    {
        var name = typed.Trim();
        if (name.Length > MaxKeyName) name = name[..MaxKeyName];
        return name.Length == 0 ? (door.Label.Length > MaxKeyName ? door.Label[..MaxKeyName] : door.Label) : name;
    }

    /// <summary>What a key is called: its name, else its last four, else its id.</summary>
    public static string Name(DoorKey key) => key.Name ?? (key.Last4 is { } l ? "…" + l : key.Id);

    /// <summary>The key's line: made, last used or not yet, and its last four. Never the key.</summary>
    public static string KeyLine(WordBook w, DoorKey key, TimeZoneInfo zone)
    {
        var made = key.MadeAt is { } m ? When(w, m, zone) : "";
        var last4 = key.Last4 ?? "";
        return key.LastUsedAt is { } used
            ? w.Get("door.api.key_line", ("made", made), ("used", When(w, used, zone)), ("last4", last4))
            : w.Get("door.api.key_never_used", ("made", made), ("last4", last4));
    }

    /// <summary>Today against its cap: tafs cloud's counted day when it sends one, else the door's.</summary>
    public static int Today(Door door) => door.CountedToday ?? door.Today;

    /// <summary>"37 of 100".</summary>
    public static string OfLimit(WordBook w, Door door) =>
        w.Count("door.api.of_limit", Today(door), ("limit", door.Limit.ToString("N0", Locale.Culture(w.Language))));

    /// <summary>The last request in tafs cloud's words only: none until it sends them, never the raw status.</summary>
    public static string? LastRequestLine(WordBook w, Door door, TimeZoneInfo zone) =>
        door.LastRequest is { Words: { } said } last
            ? w.Get("door.api.last_request", ("time", last.At is { } at ? When(w, at, zone) : ""), ("status", said)) : null;

    /// <summary>A time from tafs cloud in the account's zone, as the iPad's abbreviated date and short time.</summary>
    public static string When(WordBook w, DateTimeOffset at, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(at, zone).ToString("d MMM yyyy, HH:mm", Locale.Culture(w.Language));

    /// <summary>Where the door's caller sends work: its address on tafs cloud, whole.</summary>
    public static string Endpoint(Door door, Uri cloud) => Uri.TryCreate(cloud, door.Address, out var u) ? u.AbsoluteUri : door.Address;

    /// <summary>An example request. It reads the key from the caller's own secret, <c>$TAFS_API_KEY</c>: the key is never written in it.</summary>
    public static string Example(string endpoint) =>
        "curl -X POST " + endpoint + " \\\n  -H \"Authorization: Bearer $TAFS_API_KEY\" \\\n  -H \"Idempotency-Key: <your request id>\" \\\n"
        + "  -H \"Content-Type: application/json\" \\\n  -d '{\"input\": {}}'";
}
