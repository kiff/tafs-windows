using System.Text.Json;
using Tafs.Core.Record;
using Tafs.Core.Taf;

namespace Tafs.Core.Tests.Record;

/// <summary>The Record's search and its cases, as the iPad's RecordSearchTests read tafs cloud's answers.</summary>
[Part("record")]
public class RecordSearchTests
{
    static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;

    const string Hit = """{"id": "e1", "kind": "done", "title": "Refunded order 42", "detail": "€20", "time": "09:40", "amount_text": "€20.00", "taf": {"id": "taf-1", "name": "Rae"}, "about": "order 42"}""";

    [Fact]
    public void A_page_reads_tafs_cloud_s_groups_and_words()
    {
        var page = RecordSearch.ReadPage(J($$"""{"q": "42", "hint": " Search by order ", "groups": [{"id": "g", "title": "Today", "entries": [{{Hit}}, {"id": "e2", "title": "no taf"}]}], "next_cursor": "c2", "end_words": "That's all.", "none_words": "Nothing matched."}"""));
        Assert.Equal(("42", "Search by order", "c2", "That's all.", "Nothing matched."), (page.Q, page.Hint, page.NextCursor, page.EndWords, page.NoneWords));
        var group = Assert.Single(page.Groups);
        Assert.Equal("Today", group.Title);
        var hit = Assert.Single(group.Hits);
        Assert.Equal(("taf-1", "Rae", "order 42", "Refunded order 42", EntryKind.Done, "€20.00"), (hit.TafId, hit.TafName, hit.About, hit.Line.Title, hit.Line.Kind, hit.Line.Amount));
        Assert.False(page.IsEmpty);
    }

    [Fact]
    public void Entries_with_no_groups_read_as_one_and_nothing_found_is_empty()
    {
        var plain = RecordSearch.ReadPage(J($$"""{"entries": [{{Hit}}]}"""));
        Assert.Equal("", Assert.Single(plain.Groups).Title);
        var none = RecordSearch.ReadPage(J("""{"q": "zz", "groups": [], "none_words": "Nothing matched.", "about": "  "}"""));
        Assert.True(none.IsEmpty);
        Assert.Null(none.NextCursor);
    }

    [Fact]
    public void A_case_reads_its_steps_and_what_didn_t_go_through()
    {
        var c = RecordSearch.ReadCase(J($$$"""{"about": "order 42", "steps": [{{{Hit}}}], "failed": {"id": "e1", "words": "The refund didn't go through."}}"""))!;
        Assert.Equal(("order 42", "The refund didn't go through."), (c.About, c.FailedWords));
        Assert.Single(c.Steps);
        Assert.Null(RecordSearch.ReadCase(J("""{"steps": []}""")));
        Assert.Null(RecordSearch.ReadCase(J("""{"about": "x", "failed": {"words": " "}}"""))!.FailedWords);
    }

    [Fact]
    public void Paths_escape_what_s_typed()
    {
        Assert.Equal("/v1/record?q=order%2042%20%26%20more", RecordSearch.Path("order 42 & more"));
        Assert.Equal("/v1/record?q=42&cursor=a%2Fb", RecordSearch.Path("42", "a/b"));
        Assert.Equal("/v1/record?q=&limit=1", RecordSearch.Path("", limit: 1));
        Assert.Equal("/v1/record/cases/order%2042", RecordSearch.CasePath("order 42"));
    }
}
