using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Account;

/// <summary>A device signed in as this person on the account, as the iPad's <c>KIFFClient.Device</c>.</summary>
public sealed record Device(string Id, string? Name, DateTimeOffset? LastUsed, bool ThisDevice);

/// <summary>
/// Account › Devices, as the iPad's <c>DevicesView</c>: each device signed in as this person, with when it was last used,
/// and which one is this PC.
/// </summary>
public static class Devices
{
    public const string Path = "/v1/me/devices";

    /// <summary>Signing a device out: an approval at KIFF, never for this PC itself (which signs out from Account).</summary>
    public static string DevicePath(string id) => Path + "/" + CloudRead.PathPart(id);

    /// <summary>
    /// <c>{"devices": […]}</c>, as the iPad decodes it: each needs its id. Its name is KIFF's <c>display_name</c> in the app's
    /// language, else the name it gave at sign-in; last used is <c>last_used_at</c>, else <c>last_seen_at</c> for a session from
    /// before devices; <c>this_device</c> false when not said. A field of the wrong kind fails the read.
    /// </summary>
    public static IReadOnlyList<Device>? Read(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object || !answer.TryGetProperty("devices", out var list) || list.ValueKind != JsonValueKind.Array) return null;
        var devices = new List<Device>();
        foreach (var d in list.EnumerateArray())
        {
            if (d.ValueKind != JsonValueKind.Object || !Text(d, "id", out var id) || id is null || !Text(d, "display_name", out var display)
                || !Text(d, "name", out var name) || !Time(d, "last_used_at", out var used) || !Time(d, "last_seen_at", out var seen))
                return null;
            bool thisDevice = false;
            if (d.TryGetProperty("this_device", out var t) && t.ValueKind != JsonValueKind.Null)
            {
                if (t.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
                thisDevice = t.GetBoolean();
            }
            devices.Add(new(id, display ?? name, used ?? seen, thisDevice));
        }
        return devices;
    }

    /// <summary>An optional text: absent or null is none; anything but text fails.</summary>
    static bool Text(JsonElement e, string name, out string? value)
    {
        value = null;
        if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return true;
        if (v.ValueKind != JsonValueKind.String) return false;
        value = v.GetString();
        return true;
    }

    /// <summary>An optional time: absent or null is none; anything but a time fails.</summary>
    static bool Time(JsonElement e, string name, out DateTimeOffset? value)
    {
        value = null;
        if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return true;
        value = Lenient.At(e, name);
        return value is not null;
    }

    /// <summary>Its name, or "A device".</summary>
    public static string Name(WordBook w, Device d) => d.Name ?? w.Get("settings.devices.unnamed");

    /// <summary>"Last used 3 hours ago", or "Not used yet" (the iPad's <c>lastUsed</c>).</summary>
    public static string LastUsed(WordBook w, DateTimeOffset? at, DateTimeOffset now) =>
        at is { } then ? w.Get("settings.devices.last_used", ("ago", RelativeTime.Ago(w.Language, then, now))) : w.Get("settings.devices.not_used");
}
