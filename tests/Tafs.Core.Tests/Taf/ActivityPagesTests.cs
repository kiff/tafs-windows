using System.Text.Json;
using Tafs.Core.Contracts;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>A taf's Activity read page after page, as the iPad's SourcePages and loadActivityPage.</summary>
[Part("record")]
public class ActivityPagesTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static readonly JsonElement Contract = ContractFile.Answer(SharedFiles.Contract("taf-activity.json"));

    static JsonElement Page(string cursor, string? next, string title = "Read a case") => JsonDocument.Parse($$"""
        {"groups": [{"title": "", "entries": [{"id": "{{cursor}}", "kind": "done", "title": "{{title}}"}]}],
         "filters": [{"id": "all", "title": "All"}], "filter": "all"{{(next is null ? "" : $", \"next_cursor\": \"{next}\"")}}}
        """).RootElement;

    [Fact]
    public async Task The_newest_page_reads_with_its_filters_days_and_end_words()
    {
        var asked = new List<string>();
        var pages = new ActivityPages();

        await pages.LoadAsync("taf-1", "all", path => { asked.Add(path); return Task.FromResult(CloudRead.Of(Contract)); });

        Assert.Equal(new[] { "/v1/tafs/taf-1/activity?filter=all" }, asked);
        Assert.Equal(new[] { "all", "done", "held", "refused" }, pages.Filters.Select(f => f.Id));
        Assert.Equal("Read a case", Assert.Single(Assert.Single(pages.Groups).Lines).Title);
        Assert.Equal(new ActivityEnd(ActivityEndKind.EndWords, "Nothing older in this view"), pages.End(En));
    }

    [Fact]
    public async Task More_follows_the_last_page_s_cursor_and_keeps_the_pages_before()
    {
        var asked = new List<string>();
        var pages = new ActivityPages();
        Task<CloudRead> Read(string path)
        {
            asked.Add(path);
            return Task.FromResult(CloudRead.Of(path.Contains("cursor=") ? Page("c2", null, "Older") : Page("c1", "c 2/=")));
        }

        await pages.LoadAsync("taf-1", "held", Read);
        Assert.Equal(ActivityEndKind.More, pages.End(En).Kind);
        await pages.LoadAsync("taf-1", "held", Read, more: true);
        await pages.LoadAsync("taf-1", "held", Read, more: true);

        Assert.Equal(new[] { "/v1/tafs/taf-1/activity?filter=held", "/v1/tafs/taf-1/activity?filter=held&cursor=c%202%2F%3D" }, asked);
        Assert.Equal(new[] { "Read a case", "Older" }, pages.Groups.SelectMany(g => g.Lines).Select(l => l.Title));
        Assert.Equal(ActivityEndKind.None, pages.End(En).Kind);
    }

    /// <summary>A read asked for while one is going waits for it, then follows.</summary>
    [Fact]
    public async Task More_asked_while_reading_follows_once_the_read_is_in()
    {
        var first = new TaskCompletionSource<CloudRead>();
        var asked = new List<string>();
        var pages = new ActivityPages();
        Task<CloudRead> Read(string path)
        {
            asked.Add(path);
            return path.Contains("cursor=") ? Task.FromResult(CloudRead.Of(Page("c2", null))) : first.Task;
        }

        var loading = pages.LoadAsync("t", "all", Read);
        Assert.Equal(ActivityEndKind.Loading, pages.End(En).Kind);
        await pages.LoadAsync("t", "all", Read, more: true);
        first.SetResult(CloudRead.Of(Page("c1", "next")));
        await loading;

        Assert.Equal(2, asked.Count);
        Assert.Equal(2, pages.Pages.Count);
    }

    /// <summary>A more read refused with 409 starts again from the newest; any other failure keeps the pages and says why.</summary>
    [Fact]
    public async Task A_409_on_more_starts_again_and_a_failure_says_why()
    {
        var pages = new ActivityPages();
        var refuse = 409;
        Task<CloudRead> Read(string path) => Task.FromResult(path.Contains("cursor=")
            ? CloudRead.Refused(En, refuse, """{"error": "moved on"}""")
            : CloudRead.Of(Page("fresh", "next")));

        await pages.LoadAsync("t", "all", Read);
        await pages.LoadAsync("t", "all", Read, more: true);
        Assert.Single(pages.Pages);
        Assert.Null(pages.Error);

        refuse = 503;
        await pages.LoadAsync("t", "all", Read, more: true);
        Assert.Single(pages.Pages);
        Assert.Equal(new ActivityEnd(ActivityEndKind.Error, "moved on"), pages.End(En));
    }

    [Fact]
    public async Task With_no_line_and_no_end_words_it_says_nothing_here()
    {
        var pages = new ActivityPages();
        await pages.LoadAsync("t", "refused", _ => Task.FromResult(CloudRead.Of(JsonDocument.Parse("""{"groups": []}""").RootElement)));

        Assert.Equal(new ActivityEnd(ActivityEndKind.NothingHere, "Nothing here."), pages.End(En));
    }

    [Fact]
    public void The_path_escapes_as_the_iPad_s_and_keeps_tafs_cloud_s_selection() =>
        Assert.Equal("/v1/tafs/a%2Fb/activity?filter=all&tools=stripe_refund&from=2026-10-09&cursor=x%2By",
            ActivityPages.Path("a/b", "all", "x+y", "tools=stripe_refund&from=2026-10-09"));
}
