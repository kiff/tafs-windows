using System.Text.Json;

namespace Tafs.Core.Account;

/// <summary>The AI-data consent's words, tafs cloud's in the person's language.</summary>
public sealed record AiConsentWords(string Title, IReadOnlyList<string> Lines, string Allow, string NotNow, string Withdraw, string Given);

/// <summary>
/// The person's AI-data consent, from <c>GET /v1/me/ai-consent</c> (kiff/tafs <c>docs/accounts.md</c>, "Your data and the
/// AI"), as the iPad's <c>AIConsent</c>: given or not, the version it's for, and its words. Account shows it given with
/// Withdraw, or not given with its screen.
/// </summary>
public sealed record AiConsent(bool Given, string Version, AiConsentWords Words)
{
    public const string Path = "/v1/me/ai-consent";

    /// <summary>One the app can draw: its version, and the words for its state.</summary>
    public bool Drawable => Version.Length > 0 && (Given ? Words.Given.Length > 0 : Words.Title.Length > 0 && Words.Allow.Length > 0);

    /// <summary>Allow, for the version the person read: <c>POST {version}</c>.</summary>
    public byte[] AllowBody() => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["version"] = Version });

    /// <summary><c>{consent: {given, version, words: {title, lines, allow, not_now, withdraw, given}}}</c>; none for another shape.</summary>
    public static AiConsent? Read(JsonElement answer)
    {
        if (Obj(answer, "consent") is not { } c) return null;
        var w = Obj(c, "words") ?? default;
        var lines = w.ValueKind == JsonValueKind.Object && w.TryGetProperty("lines", out var l) && l.ValueKind == JsonValueKind.Array
            ? l.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim()).Where(x => x.Length > 0).ToList()
            : [];
        return new(c.TryGetProperty("given", out var g) && g.ValueKind == JsonValueKind.True, T(c, "version"),
            new(T(w, "title"), lines, T(w, "allow"), T(w, "not_now"), T(w, "withdraw"), T(w, "given")));
    }

    internal static string T(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";

    internal static JsonElement? Obj(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;
}

/// <summary>
/// The account's "Show suggestions" setting, from <c>GET /v1/me/suggestions</c>, as the iPad's <c>TafsCloud.Suggestions</c>:
/// whether a new Coach conversation offers recipes, and tafs cloud's words for what the switch does now. Someone who
/// manages changes it (<c>PUT {show}</c>).
/// </summary>
public sealed record Suggestions(bool Show, string Words)
{
    public const string Path = "/v1/me/suggestions";

    public static byte[] Body(bool show) => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, bool> { ["show"] = show });

    /// <summary><c>{suggestions: {show, reason, words}}</c>; none for another shape. On when it doesn't say, as tafs cloud's default.</summary>
    public static Suggestions? Read(JsonElement answer) =>
        AiConsent.Obj(answer, "suggestions") is { } s
            ? new(!(s.TryGetProperty("show", out var v) && v.ValueKind == JsonValueKind.False), AiConsent.T(s, "words"))
            : null;
}
