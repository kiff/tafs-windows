using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Tafs.Core.Tests.Ci;

/// <summary>
/// This repository is public: its code, tests, docs and workflows state each rule itself, never who asked for it, when, or
/// where it was discussed. A link to the private coordination issues, a credit to the project's manager, a rule dated by
/// when the owner set it, or a team's own name for one of its lanes fails here, so it never rests on a reviewer noticing.
/// The names are matched by the first 16 hex digits of their SHA-256, so this file never spells one out.
/// <c>.github/CODEOWNERS</c> is read for everything but names: it works by GitHub logins.
/// </summary>
[Part("ci")]
public partial class PublicTextTests
{
    /// <summary>The folders and files read, and the kinds of file in them that are text.</summary>
    static readonly string[] Places = ["src", "tests", "docs", "tools", "installer", ".github", "README.md"];
    static readonly string[] TextKinds = [".cs", ".xaml", ".md", ".yml", ".yaml", ".ps1", ".wxs", ".wxl", ".json", ".xml", ".txt", ".csproj", ".props", ".targets", ".manifest", ".appxmanifest", ""];
    const string Owners = ".github/CODEOWNERS";
    /// <summary>This file: its patterns and examples say what's caught, so it's the one place they may be written.</summary>
    const string Self = "tests/Tafs.Core.Tests/Ci/PublicTextTests.cs";

    /// <summary>The names, as the first 16 hex digits of each one's SHA-256. To add one: <c>printf %s NAME | sha256sum | cut -c1-16</c>.</summary>
    static readonly HashSet<string> Names =
    [
        "3c32e58c6fc3cf62", "20cca48ece26c362", "f5068c4b858ead38", "fe9c6bd7a66301f4", "50077b782480811e",
        "be896ac583d76627", "afb42e3f1a4bce7e", "d419916f176e52d3", "bd184cdc7aea9ef8", "ab63a9b276c86bec",
        "2ad06b22b301f873", "3c00d0a9d4c2eb08", "018a7f9527b114ab", "94c6cdf43ac3f929", "4a25962615e31ac6",
        // The reviewers' own logins: GitHub reads them in CODEOWNERS, which isn't checked for names; nowhere else names them.
        "53dd1be914bd441c", "f66e74c6b8883a1f", "3726111e555e250c",
    ];

    /// <summary>What a line may never say, names aside: the private issues, a manager's credit, a rule dated by its day.</summary>
    [GeneratedRegex(@"kiff/work|\bthe PM\b|\bPM's\b|\(PM[ ,):]|\bowner(?:'s|,) [0-9]{1,2} (Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\b")]
    private static partial Regex Said();

    /// <summary>A word as a name would be written: lowercase letters and digits, joined by hyphens, not part of a longer one.</summary>
    [GeneratedRegex(@"(?<![@\w-])[a-z0-9]+(?:-[a-z0-9]+)*(?![\w-])")]
    private static partial Regex Word();

    static string Hash(string word) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(word)))[..16].ToLowerInvariant();

    /// <summary>Why a line can't stay, or null.</summary>
    static string? Problem(string line, bool names = true)
    {
        if (Said().IsMatch(line)) return "a thread, a manager's credit or a dated rule";
        if (names && Word().Matches(line).Any(m => Names.Contains(Hash(m.Value)))) return "a team's name for one of its lanes";
        return null;
    }

    [Fact]
    public void The_repository_names_no_lane_thread_manager_or_dated_rule()
    {
        var root = RepoFiles.Root;
        var found = new List<string>();
        foreach (var file in Files(root))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (Problem(lines[i], names: rel != Owners) is { } why)
                    found.Add($"{rel}:{i + 1}: {why}: {lines[i].Trim()}");
        }
        Assert.True(found.Count == 0, "State the rule itself instead:\n" + string.Join("\n", found));
    }

    [Theory]
    [InlineData("see kiff/work#20")]
    [InlineData("as the PM's ruling")]
    [InlineData("pricing (PM)")]
    [InlineData("the owner's 22 Nov rule")]
    public void A_thread_a_credit_or_a_date_is_caught(string line) => Assert.NotNull(Problem(line));

    [Fact]
    public void A_name_is_caught_by_its_hash_and_never_written_here()
    {
        // The names themselves never appear in this file, so a name is checked through a word whose hash is added for the test.
        var made = "a-made-up-name";
        Names.Add(Hash(made));
        try
        {
            Assert.NotNull(Problem($"as {made}'s table has it"));
            Assert.Null(Problem($"as {made}-plus has it"));
            Assert.Null(Problem($"@{made}"));
        }
        finally { Names.Remove(Hash(made)); }
        Assert.All(Names, n => Assert.Matches("^[0-9a-f]{16}$", n));
    }

    [Theory]
    [InlineData("the owner's rule: approve on your phone")]
    [InlineData("kiff/cloud#23 and kiff/planque#23 are public issues")]
    [InlineData("tafs-linux's table, the same as Windows'")]
    [InlineData("3 PM in the afternoon")]
    public void Plain_text_passes(string line) => Assert.Null(Problem(line));

    static IEnumerable<string> Files(string root)
    {
        foreach (var place in Places)
        {
            var path = Path.Combine(root, place);
            if (File.Exists(path)) { yield return path; continue; }
            if (!Directory.Exists(path)) continue;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (rel.Contains("/bin/") || rel.Contains("/obj/") || rel == Self) continue;
                if (TextKinds.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) yield return file;
            }
        }
    }
}
