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

    /// <summary>
    /// "Your Chrome" 3 and 4: what a Chrome is used for, and what unpairing it ends, are planque's list of its live connections
    /// (planque#370), never this PC's own rule.
    /// </summary>
    [Fact]
    public void A_chrome_says_what_it_s_used_for_from_planque_s_own_list()
    {
        var list = PlanqueChromes.Read(JsonDocument.Parse("""
            {"chromes":[
              {"chrome_id":"chr_1","name":"Work laptop","connections":[
                {"grant":"g1","name":"Wiki lookup","sites":["wikipedia.org"]},
                {"grant":"g2","name":"Shop","sites":["shop.example","wikipedia.org"]},
                {"name":"no grant","sites":["x.example"]},
                {"grant":"g3","name":"Odd","sites":[1, "", "news.example"]}]},
              {"chrome_id":"chr_2","name":"Home","connections":[]},
              {"chrome_id":"chr_3","name":"Older planque"}]}
            """).RootElement)!;

        Assert.Equal(["g1", "g2", "g3"], list[0].Connections!.Select(u => u.Grant));
        Assert.Equal(["news.example"], list[0].Connections![2].Sites);
        // Each site once, in planque's order.
        Assert.Equal("Used for wikipedia.org, shop.example, news.example", PlanqueChromes.UsedFor(En, list[0]));
        Assert.Equal("Unpairing ends the connections that use it: wikipedia.org, shop.example, news.example. They come back only when you connect them again.",
            PlanqueChromes.UnpairEnds(En, list[0]));

        // Nothing runs in it, or planque doesn't say: nothing is written, and Unpair asks as before.
        Assert.Empty(list[1].Connections!);
        Assert.Null(list[2].Connections);
        foreach (var quiet in list.Skip(1))
        {
            Assert.Null(PlanqueChromes.UsedFor(En, quiet));
            Assert.Null(PlanqueChromes.UnpairEnds(En, quiet));
        }
    }

    /// <summary>A grant still says which Chrome drives it (<c>request.browser</c>), as planque sends it.</summary>
    [Fact]
    public void A_grant_names_the_chrome_that_drives_it()
    {
        var json = """
            {"grants": [{"id": "g1", "status": "connected", "text": "t", "digest": "d", "events": [], "request": {"server": "https://planque.kiff.dev/browser",
              "agent": "a", "tools": [], "browser": {"driver": "extension", "chrome": "chr_1", "chrome_name": "Work laptop", "sites": ["wikipedia.org"]}}},
              {"id": "g4", "status": "connected", "text": "t", "digest": "d", "events": [], "request": {"server": "s", "agent": "a", "tools": [], "browser": {"sites": ["news.example"]}}}]}
            """;
        var grants = PlanqueGrants.Read(System.Text.Encoding.UTF8.GetBytes(json))!;
        Assert.Equal(("chr_1", "Work laptop"), (grants[0].Chrome!.ChromeId, grants[0].Chrome!.ChromeName));
        Assert.Null(grants[1].Chrome);
    }
}
