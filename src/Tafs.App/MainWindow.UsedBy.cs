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

    /// <summary>The Overview's rows with who uses each taf where it's been read, and no line anywhere else.</summary>
    IReadOnlyList<OverviewRow> WithUsedBy(IReadOnlyList<OverviewRow> rows) =>
        rows.Select(r => r with { UsedBy = usedBy.GetValueOrDefault(r.Key) }).ToList();

    /// <summary>
    /// Reads who uses each taf this person manages, then draws the Overview's rows again. A taf they no longer manage loses
    /// its line first; when KIFF's groups or people can't be read, every line goes, and a taf whose shares can't be read has
    /// none, rather than something untrue or out of date. One that lands after another session began is dropped.
    /// </summary>
    async Task ReadUsedByAsync(HomeAnswer listed, int reading)
    {
        var mine = kiffRead is not null && manage is { } managed
            ? listed.Tafs.Where(t => t.Agent is { } agent && managed.GetValueOrDefault(agent)).ToList()
            : [];
        Shares.KeepManaged(usedBy, mine.Select(t => t.Id));
        ShowUsedBy();
        if (mine.Count == 0 || kiffRead is not { } read) return;
        var groupsRead = await read(People.GroupsPath);
        var peopleRead = await read(People.PeoplePath);
        if (reading != sessions) return;
        var groups = groupsRead.Answer is { } g ? People.Groups(g) : null;
        var people = peopleRead.Answer is { } p ? People.Members(p) : null;
        if (groups is null || people is null)
        {
            usedBy.Clear();
            ShowUsedBy();
            return;
        }
        foreach (var taf in mine)
        {
            var sharesRead = await read(Shares.Path(taf.Agent!));
            if (reading != sessions) return;
            if (Shares.UsedBy(App.Words, sharesRead.Answer is { } s ? Shares.Read(s) : null, groups, people) is { } line) usedBy[taf.Id] = line;
            else usedBy.Remove(taf.Id);
        }
        ShowUsedBy();
    }

    /// <summary>The Overview's rows drawn again with the lines as they are now.</summary>
    void ShowUsedBy()
    {
        overviewRows = WithUsedBy(overviewRows);
        if (Pages.Content is OverviewPage overview) overview.ShowRows(overviewRows, home?.Labels);
    }
}
