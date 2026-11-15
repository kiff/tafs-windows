using System.Text.Json;

namespace Tafs.Core.Taf;

/// <summary>Reads tafs cloud's answers leniently: a field that's missing or of another type reads as none, never the whole answer.</summary>
static class Lenient
{
    public static IEnumerable<JsonElement> Array(JsonElement o, string name) =>
        Field(o, name) is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray() : [];

    public static string? Text(JsonElement o, string name) =>
        Field(o, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    public static int? Int(JsonElement o, string name) =>
        Field(o, name) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out var n) ? n : null;

    public static long? Long(JsonElement o, string name) =>
        Field(o, name) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt64(out var n) ? n : null;

    /// <summary>A time as tafs cloud writes it (ISO 8601, UTC when it says no offset).</summary>
    public static DateTimeOffset? At(JsonElement o, string name) =>
        DateTimeOffset.TryParse(Text(o, name), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var at) ? at : null;

    public static bool Bool(JsonElement o, string name) => Field(o, name) is { ValueKind: JsonValueKind.True };

    public static JsonElement? Field(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) ? v : null;
}
