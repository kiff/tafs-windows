using System.Text.Json;
using Tafs.Core.Taf;

namespace Tafs.Core.Account;

/// <summary>
/// The KIFF account this PC is signed in to, from <c>GET /v1/me</c>, as the iPad's <c>KIFFClient.Account</c>: its id and slug,
/// its display name, the person's email, and the account's time zone, which ends a Card's day.
/// </summary>
public sealed record KiffAccount(string TenantId, string Slug, string? DisplayName, string? Email, string? TimeZone)
{
    /// <summary>The account as the owner knows it: its name, else its slug.</summary>
    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Slug : DisplayName!.Trim();

    /// <summary>The account, read leniently: none without its id and slug; every other field reads as none when it isn't text.</summary>
    public static KiffAccount? Read(JsonElement me)
    {
        static string? T(JsonElement o, string name) =>
            o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            && v.GetString()!.Trim() is { Length: > 0 } t ? t : null;
        return T(me, "tenant_id") is { } id && T(me, "tenant_slug") is { } slug
            ? new(id, slug, T(me, "tenant_display_name"), T(me, "email"), T(me, "time_zone")) : null;
    }

    /// <summary>
    /// The email under the account's name, as the iPad's <c>AccountLines.signedInAs</c>: none when the account is named after
    /// it (a Solo account), so it isn't said twice, or when KIFF sent none.
    /// </summary>
    public string? SignedInAs => Email is { } email && !string.Equals(email, Name, StringComparison.OrdinalIgnoreCase) ? email : null;

    /// <summary>Under Account in the sidebar: the person's email, else the account's name.</summary>
    public string SidebarLine => Email ?? Name;
}
