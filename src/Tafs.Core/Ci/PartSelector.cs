namespace Tafs.Core.Ci;

/// <summary>Picks the test parts a change must run: the parts it touches, plus the always-on ones.</summary>
public static class PartSelector
{
    public static IReadOnlyList<string> Select(PartsConfig config, IEnumerable<string> changedFiles)
    {
        var files = changedFiles.Select(f => f.Replace('\\', '/')).Where(f => f.Length > 0).ToList();
        var chosen = new HashSet<string>(config.Always);

        foreach (var file in files)
        {
            if (config.AllOn.Any(g => Glob.IsMatch(g, file))) return All(config);

            var owners = config.Parts.Where(p => p.Value.Any(g => Glob.IsMatch(g, file))).Select(p => p.Key).ToList();
            if (owners.Count > 0) { chosen.UnionWith(owners); continue; }

            // Code that belongs to no part is not safe to skip: run everything.
            if (config.CodeRoots.Any(root => file.StartsWith(root, StringComparison.Ordinal))) return All(config);
            // Anything else (docs, README, images) touches no tests.
        }

        return config.Parts.Keys.Where(chosen.Contains).ToList();
    }

    public static IReadOnlyList<string> All(PartsConfig config) => config.Parts.Keys.ToList();
}
