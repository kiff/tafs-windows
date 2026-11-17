using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tafs.Core.Home;

/// <summary>A newer published tafs for Windows: its tag (v1.0.2) and the release page the sidebar's line opens.</summary>
public sealed record NewVersion(string Tag, Uri Page);

/// <summary>
/// "A new version of tafs is out" (docs/app.md, A new version): tell only, as tafs for Linux does. The app reads GitHub's
/// latest published release of tafs-windows at start and once a day, and when it's newer than this app, the sidebar's foot
/// says so in one line that opens that release's page. Nothing is downloaded, installed or run, and nothing about the
/// person, the account or this PC is sent: only the app's own User-Agent.
/// </summary>
public static class NewVersions
{
    /// <summary>GitHub's public answer for the latest published release: never a draft or a prerelease.</summary>
    public static readonly Uri Latest = new("https://api.github.com/repos/kiff/tafs-windows/releases/latest");

    /// <summary>The releases page, opened when a release's own page isn't one of ours.</summary>
    public static readonly Uri Releases = new("https://github.com/kiff/tafs-windows/releases");

    /// <summary>How often the app asks again while it stays open.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromDays(1);

    // ASCII digits only: .NET's \d would also take other scripts' digits.
    static readonly Regex Tag = new(@"^v([0-9]+)\.([0-9]+)\.([0-9]+)$", RegexOptions.CultureInvariant);

    /// <summary>The User-Agent, the only thing the request says: the app and its version (tafs-windows/1.0.0).</summary>
    public static string UserAgent(Version? running) => $"tafs-windows/{Three(running)}";

    /// <summary>A release's version from its tag: "v" and three numbers, else none (v1.0.2-rc1, nightly and v1.0 aren't).</summary>
    public static Version? Of(string? tag) =>
        tag is not null && Tag.Match(tag) is { Success: true } m
        && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
        && int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
        && int.TryParse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch)
            ? new Version(major, minor, patch)
            : null;

    /// <summary>
    /// The newer release in GitHub's answer, or none: none when it's a draft or a prerelease (or doesn't say it isn't), when
    /// its tag isn't a version, when it's this app's version or older, or when the answer isn't a release at all.
    /// </summary>
    public static NewVersion? Read(string? json, Version? running)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            // Published only: both must say false; a missing one counts as not published.
            if (!IsFalse(r, "draft") || !IsFalse(r, "prerelease")) return null;
            var tag = r.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (Of(tag) is not { } version || running is null || version <= Three(running)) return null;
            var url = r.TryGetProperty("html_url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            return new NewVersion(tag!, PageOf(tag!, url));
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The page the line opens: the release's own, only when it's ours exactly; else the releases page.</summary>
    public static Uri PageOf(string tag, string? htmlUrl) =>
        htmlUrl == $"https://github.com/kiff/tafs-windows/releases/tag/{tag}" && Uri.TryCreate(htmlUrl, UriKind.Absolute, out var page)
            ? page
            : Releases;

    static bool IsFalse(JsonElement r, string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.False;

    /// <summary>This app's version as a release's: its first three numbers (a release build is 1.0.0.&lt;run&gt;).</summary>
    static Version Three(Version? v) => v is null ? new Version(0, 0, 0) : new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
}
