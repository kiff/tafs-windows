using Tafs.Core.Account;
using Tafs.Core.Home;

namespace Tafs.App;

/// <summary>
/// Who uses each taf, under its name on the Overview, as the iPad's <c>usedBy</c>: only for whoever manages it, as on its page
/// (someone who uses it doesn't see who else does). KIFF's groups and people, read once, then each managed taf's shares.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The line under each managed taf's name, by taf id, as last read.</summary>
    readonly Dictionary<string, string> usedBy = [];

    /// <summary>The Overview's rows with who uses each taf, where it's been read.</summary>
    IReadOnlyList<OverviewRow> WithUsedBy(IReadOnlyList<OverviewRow> rows) =>
        rows.Select(r => usedBy.TryGetValue(r.Key, out var who) ? r with { UsedBy = who } : r).ToList();

    /// <summary>
    /// Reads who uses each taf this person manages, then draws the Overview's rows again. Any read that fails says nothing for
    /// that taf, rather than something untrue; one that lands after another session began is dropped.
    /// </summary>
    async Task ReadUsedByAsync(HomeAnswer listed, int reading)
    {
        if (kiffRead is not { } read || manage is not { } managed) return;
        var mine = listed.Tafs.Where(t => t.Agent is { } agent && managed.GetValueOrDefault(agent)).ToList();
        if (mine.Count == 0) return;
        var groupsRead = await read(People.GroupsPath);
        var peopleRead = await read(People.PeoplePath);
        if (reading != sessions) return;
        var groups = groupsRead.Answer is { } g ? People.Groups(g) : null;
        var people = peopleRead.Answer is { } p ? People.Members(p) : null;
        if (groups is null || people is null) return;
        foreach (var taf in mine)
        {
            var sharesRead = await read(Shares.Path(taf.Agent!));
            if (reading != sessions) return;
            if (Shares.UsedBy(App.Words, sharesRead.Answer is { } s ? Shares.Read(s) : null, groups, people) is { } line) usedBy[taf.Id] = line;
            else usedBy.Remove(taf.Id);
        }
        overviewRows = WithUsedBy(overviewRows);
        if (Pages.Content is OverviewPage overview) overview.ShowRows(overviewRows, home?.Labels);
    }
}
