using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Account;

/// <summary>
/// One of the account's passkeys, as the iPad's <c>KIFFClient.Passkey</c>: its id, its name, and the computer it belongs to.
/// Only one of the person's own can be removed; a computer's goes with its computer.
/// </summary>
public sealed record AccountPasskey(string? Id, string? Name, string? DeviceId)
{
    public bool Removable => Id is not null && DeviceId is null;
}

/// <summary>
/// Account › Devices' passkeys (KIFF's pairing contract, "Registering one"), as the iPad's <c>DevicesView</c>: each by its
/// name, "Goes with its computer" for a computer's own, and Remove for the person's own, for an owner or an admin on a PC that
/// approves. Adding one is the phone's.
/// </summary>
public static class Passkeys
{
    public const string Path = "/v1/me/passkeys";

    public static string RemovePath(string id) => Path + "/" + CloudRead.PathPart(id);

    /// <summary><c>{"passkeys": […]}</c>, as the iPad decodes it: each field optional; a missing list is none. A field of the wrong kind fails the read.</summary>
    public static IReadOnlyList<AccountPasskey>? Read(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object) return null;
        if (!answer.TryGetProperty("passkeys", out var list) || list.ValueKind == JsonValueKind.Null) return [];
        if (list.ValueKind != JsonValueKind.Array) return null;
        var passkeys = new List<AccountPasskey>();
        foreach (var p in list.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object || !Text(p, "id", out var id) || !Text(p, "name", out var name) || !Text(p, "device_id", out var device))
                return null;
            passkeys.Add(new(id, name, device));
        }
        return passkeys;
    }

    static bool Text(JsonElement e, string name, out string? value)
    {
        value = null;
        if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return true;
        if (v.ValueKind != JsonValueKind.String) return false;
        value = v.GetString();
        return true;
    }

    /// <summary>Its name, else "A device", as the iPad's <c>passkeyName</c>.</summary>
    public static string Name(WordBook w, AccountPasskey p) => p.Name ?? w.Get("settings.devices.unnamed");

    /// <summary>Who may remove one, as the iPad's <c>canAddPasskey</c>: an owner or an admin, on a session that approves.</summary>
    public static bool MayRemove(string? role, bool approves) => approves && role is "owner" or "admin";
}

/// <summary>
/// Signing a device out, or removing a passkey, from this PC (P11): each is an approval at KIFF (<c>DELETE /v1/me/devices/{id}</c>,
/// <c>DELETE /v1/me/passkeys/{id}</c>, in its phone approval rules), so this PC's own passkey approves exactly that request
/// behind Windows Hello, as the iPad's approval key signs <c>prepareRemoveDevice</c> and <c>preparePasskeyRemoval</c>.
/// </summary>
public static class AccountRemoval
{
    /// <summary>
    /// None when it went; else why, in the iPad's words: "Cancelled. Nothing changed." for Windows Hello closed or anything that
    /// kept it from signing, and "Sign … out of your account didn't go through." with KIFF's reason for a refusal. A sign-out
    /// meanwhile ends it quietly.
    /// </summary>
    /// <param name="what">What's asked, as the prompt names it ("Sign Laptop out of your account").</param>
    public static async Task<(bool Done, string? Why)> RemoveAsync(WordBook w, string path, string what,
        Func<string, Task<(string Header, string Value)>> sign,
        Func<string, (string Header, string Value), Task<CloudRead>> delete,
        Func<bool> current)
    {
        (string Header, string Value) signature;
        try { signature = await sign(path); }
        catch (Exception e) when (e is OperationCanceledException or PasskeyRefusedException or InvalidDataException or COMException
            or CryptographicException or InvalidOperationException or UnauthorizedAccessException)
        {
            return (false, current() ? w.Get("approval.cancelled") : null);
        }
        if (!current()) return (false, null);
        var answer = await delete(path, signature);
        if (!current()) return (false, null);
        // A removal answers 204, with nothing to read.
        if (answer.Status is >= 200 and <= 299) return (true, null);
        return (false, w.Get("common.didnt_go_through", ("what", what), ("why", answer.Why ?? w.Get("error.ours"))));
    }
}
