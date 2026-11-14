using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tafs.Core.Ci;

/// <summary>
/// The commit of the shared repository whose words and contracts this app reads.
/// Stored in shared-pin.json; bumping it is one small PR.
/// </summary>
public sealed record SharedPin(string Repo, string Commit, IReadOnlyList<string> Paths)
{
    static readonly Regex Sha = new("^[0-9a-f]{40}$", RegexOptions.Compiled);

    public static SharedPin Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var repo = root.GetProperty("repo").GetString() ?? "";
        var commit = root.GetProperty("commit").GetString() ?? "";
        var paths = root.GetProperty("paths").EnumerateArray().Select(p => p.GetString() ?? "").ToList();

        if (!Regex.IsMatch(repo, @"^[\w.-]+/[\w.-]+$"))
            throw new InvalidDataException("pin: repo must be owner/name");
        if (!Sha.IsMatch(commit))
            throw new InvalidDataException("pin: commit must be a full 40-character lowercase sha, not a branch or tag");
        if (paths.Count == 0 || paths.Any(p => p.Length == 0 || p.StartsWith('/') || p.Contains("..")))
            throw new InvalidDataException("pin: paths must be a non-empty list of relative paths");
        return new SharedPin(repo, commit, paths);
    }
}
