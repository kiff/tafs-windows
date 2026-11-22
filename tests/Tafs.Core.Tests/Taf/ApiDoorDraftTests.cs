using System.Text;
using System.Text.Json;
using Tafs.Core.Contracts;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>Coach's drafted door (the iPad's DoorDraftCard), read from tafs cloud's pinned draft: drawn from the body it signs.</summary>
[Part("record")]
public class ApiDoorDraftTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static JsonElement Pinned() => ContractFile.Answer(SharedFiles.Contract("doors-api-draft.json"));

    [Fact]
    public void The_pinned_draft_is_drawn_from_its_body()
    {
        var d = ApiDoorDrafts.Read(Pinned())!;
        Assert.Equal(("draft-0123456789abcdef", "Order system", "Check the return and refund it if the Card allows"), (d.Id, d.Name, d.Job));
        Assert.Equal(new[] { "order · string", "reason · damaged | late | wrong_item" }, d.Fields.Select(ApiDoorDrafts.Field));
        Assert.Equal("It gets back one of: refunded, refused, and needs_review, and how many calls were made.", ApiDoorDrafts.OutcomesLine(En, d));
        Assert.False(d.Text);
    }

    [Fact]
    public void What_is_signed_is_the_body_in_its_canonical_form()
    {
        var d = ApiDoorDrafts.Read(Pinned())!;
        var body = Pinned().GetProperty("body").GetRawText();
        Assert.Equal(Tafs.Core.Kiff.Jcs.Canonical(Encoding.UTF8.GetBytes(body)), d.Body);
        var text = Encoding.UTF8.GetString(d.Body);
        Assert.StartsWith("{\"draft\":", text);
        Assert.DoesNotContain(" ", text.Replace("Order system", "").Replace("Check the return and refund it if the Card allows", ""));
    }

    [Theory]
    [InlineData("""{"draft_id":"d1","body":{"kind":"email","draft":{"name":"x","job":"y"}}}""")]
    [InlineData("""{"draft_id":"d1","body":{"kind":"api","draft":{"name":" ","job":"y"}}}""")]
    [InlineData("""{"draft_id":"d1","body":{"kind":"api","draft":{"name":"x"}}}""")]
    [InlineData("""{"body":{"kind":"api","draft":{"name":"x","job":"y"}}}""")]
    public void A_draft_that_is_not_an_api_door_s_or_lacks_its_name_or_job_is_none(string json) =>
        Assert.Null(ApiDoorDrafts.Read(JsonDocument.Parse(json).RootElement));

    [Fact]
    public void The_approval_answers_the_door_and_its_first_key()
    {
        var door = ContractFile.Answer(SharedFiles.Contract("doors-api.json")).GetProperty("doors")[0].GetRawText();
        var added = ApiDoorDrafts.ReadAdded(JsonDocument.Parse(
            "{\"door\":" + door + ",\"key\":{\"id\":\"key_9\",\"key\":\"tafs_live_x\",\"last4\":\"_x\",\"made_at\":\"2026-11-19T10:00:00Z\"}}").RootElement)!;
        Assert.Equal(("door_ord7", "tafs_live_x"), (added.Door.Id, added.Key.Key));
        Assert.Null(ApiDoorDrafts.ReadAdded(JsonDocument.Parse("{\"door\":" + door + "}").RootElement));
    }

    [Fact]
    public void A_used_draft_is_told_apart() =>
        Assert.True(ApiDoorDrafts.Used(new CloudRead(null, "This draft was used", 410, "draft_used")));
}
