using System.Text.Json;
using Tafs.Core.Kiff;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Kiff;

/// <summary>Account › Chrome (docs/chrome.md), as the iPad's ChromesView reads planque: the list, when it shows, and what a Chrome says.</summary>
[Part("approvals")]
public class PlanqueChromesTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    [Fact]
    public void The_chromes_read_as_planque_lists_them_and_one_it_can_t_read_is_left_out()
    {
        var list = PlanqueChromes.Read(JsonDocument.Parse("""
            {"chromes":[{"chrome_id":"chr_1","name":"Work laptop","browser":"Chrome 131","os":"Windows","paired_at":"2026-11-22T10:00:00Z"},
                        {"chrome_id":"chr_2","name":"Old"},{"name":"no id"}]}
            """).RootElement)!;
        Assert.Equal(new[] { "chr_1", "chr_2" }, list.Select(c => c.Id));
        Assert.Equal("Chrome 131 on Windows", PlanqueChromes.Says(En, list[0]));
        Assert.Null(PlanqueChromes.Says(En, list[1]));
        Assert.Null(PlanqueChromes.Read(JsonDocument.Parse("{}").RootElement));
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(404, false)]
    [InlineData(403, false)]
    [InlineData(0, false)]
    public void Chrome_shows_only_once_planque_lists_them(int status, bool shown) => Assert.Equal(shown, PlanqueChromes.Offered(status));

    [Fact]
    public void The_paths_are_the_account_s()
    {
        Assert.Equal("/v1/accounts/t_1/chromes", PlanqueChromes.ListPath("t_1"));
        Assert.Equal("/v1/accounts/t_1/chromes/chr%2F1", PlanqueChromes.ChromePath("t_1", "chr/1"));
    }

    /// <summary>"Your Chrome" 3 and 4: a Chrome's connections are the account's live browser grants on it, each by name and sites; unpairing ends exactly these.</summary>
    [Fact]
    public void A_chrome_lists_the_live_connections_that_run_in_it()
    {
        static string Grant(string id, string status, string title, string browser) =>
            "{\"id\": \"" + id + "\", \"status\": \"" + status + "\", \"text\": \"t\", \"digest\": \"d\", \"title\": \"" + title
            + "\", \"events\": [], \"request\": {\"server\": \"https://planque.kiff.dev/browser\", \"agent\": \"a\", \"tools\": [], \"browser\": " + browser + "}}";
        var json = "{\"grants\": [" + string.Join(",",
            Grant("g1", "connected", "Wiki lookup", """{"driver": "extension", "chrome": "chr_1", "chrome_name": "Work laptop", "sites": ["wikipedia.org"]}"""),
            Grant("g2", "disconnected", "Old", """{"driver": "extension", "chrome": "chr_1", "sites": ["old.example"]}"""),
            Grant("g3", "connected", "Elsewhere", """{"driver": "extension", "chrome": "chr_2", "sites": ["shop.example"]}"""),
            Grant("g4", "connected", "Hosted", """{"sites": ["news.example"]}""")) + "]}";
        var grants = PlanqueGrants.Read(System.Text.Encoding.UTF8.GetBytes(json))!;
        Assert.Equal(new[] { "Wiki lookup · wikipedia.org" }, PlanqueChromes.ConnectionsOn(grants, "chr_1"));
        Assert.Equal("Work laptop", grants[0].Chrome!.ChromeName);
        Assert.Null(grants[3].Chrome);
        Assert.Empty(PlanqueChromes.ConnectionsOn(grants, "chr_9"));
    }
}
