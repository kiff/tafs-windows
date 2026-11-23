using Tafs.Core.Home;

namespace Tafs.Core.NeedsYou;

/// <summary>Coach's held install (kiff/tafs#206), as the iPad's <c>beginCoachInstall</c>: the taf its version is for.</summary>
public static class CoachInstalls
{
    /// <summary>
    /// The taf Coach's hold names: by tafs cloud's id, else by its name ignoring case, as the iPad matches it, but only when
    /// exactly one taf on Home has that name: two of the same name could install on the wrong one, so then it's none. None
    /// also when no taf is either, and then nothing is installed.
    /// </summary>
    public static HomeTaf? Find(VersionInstall ask, IReadOnlyList<HomeTaf> tafs)
    {
        if (tafs.FirstOrDefault(t => t.Id == ask.Taf) is { } byId) return byId;
        var named = tafs.Where(t => string.Equals(t.Name, ask.Taf, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return named.Count == 1 ? named[0] : null;
    }
}
