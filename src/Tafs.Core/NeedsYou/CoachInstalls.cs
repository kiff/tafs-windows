using Tafs.Core.Home;

namespace Tafs.Core.NeedsYou;

/// <summary>Coach's held install (kiff/tafs#206), as the iPad's <c>beginCoachInstall</c>: the taf its version is for.</summary>
public static class CoachInstalls
{
    /// <summary>
    /// The taf Coach's hold names: by tafs cloud's id, else by its name ignoring case, as the iPad matches it. None when no taf
    /// on Home is either, and then nothing is installed.
    /// </summary>
    public static HomeTaf? Find(VersionInstall ask, IReadOnlyList<HomeTaf> tafs) =>
        tafs.FirstOrDefault(t => t.Id == ask.Taf) ?? tafs.FirstOrDefault(t => string.Equals(t.Name, ask.Taf, StringComparison.OrdinalIgnoreCase));
}
