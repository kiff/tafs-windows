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
}
