namespace Tafs.Core.Kiff;

/// <summary>The vendor's sign-in, back from planque: for which account and grant, with the one-use code this PC confirms.</summary>
public sealed record PlanqueSignedIn(string Tenant, string Grant, string Code);

/// <summary>
/// Where planque sends the browser back after a vendor's sign-in, as the iPad's <c>PlanqueClient.signedIn(from:)</c>:
/// <c>tafs://planque/signed-in?tenant=…&amp;grant=…&amp;confirm=…</c>. Signed in only once planque says the grant is
/// connected, after this PC confirms the code, signed; the URL alone connects nothing.
/// </summary>
public static class PlanqueSignIn
{
    /// <summary>The app's own URL scheme, which Windows hands back to tafs.</summary>
    public const string Scheme = "tafs";

    /// <summary>The return, or none for any other URL or one missing a part.</summary>
    public static PlanqueSignedIn? Read(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme != Scheme || url.Host != "planque" || url.AbsolutePath != "/signed-in") return null;
        var items = url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .GroupBy(p => Uri.UnescapeDataString(p[0]))
            .ToDictionary(g => g.Key, g => g.First().Length == 2 ? Uri.UnescapeDataString(g.First()[1].Replace('+', ' ')) : "");
        string? Value(string name) => items.GetValueOrDefault(name) is { Length: > 0 } v ? v : null;
        return Value("tenant") is { } tenant && Value("grant") is { } grant && Value("confirm") is { } code ? new(tenant, grant, code) : null;
    }
}
