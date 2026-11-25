using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Contracts;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Coach;

/// <summary>
/// Deleting one of Coach's conversations for good (<c>DELETE /v1/tafs/{id}/chats/{session}</c>), only in tafs cloud's words
/// (coach-chats.json's <c>delete</c> at the pin), following its answer as the iPhone's and Android's do.
/// </summary>
[Part("coach")]
public class ChatDeleteTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static JsonElement List() => ContractFile.Answer(SharedFiles.Contract("coach-chats.json"));

    static ChatDeleteWords Said() => ChatDeleteWords.Read(List())!;

    [Fact]
    public async Task The_list_read_carries_tafs_cloud_s_Delete_words()
    {
        var (rows, why, said) = await CoachChats.ReadAsync(En, "/v1/tafs/c/chats", _ => Task.FromResult(CloudRead.Of(List())));
        Assert.Null(why);
        Assert.Single(rows!);
        var d = List().GetProperty("delete");
        Assert.Equal(new ChatDeleteWords(d.GetProperty("item").GetString()!, d.GetProperty("confirm_title").GetString()!,
            d.GetProperty("confirm_body").GetString()!, d.GetProperty("confirm_button").GetString()!, d.GetProperty("failed").GetString()!), said);
    }

    [Theory]
    [InlineData("item")]
    [InlineData("confirm_title")]
    [InlineData("confirm_body")]
    [InlineData("confirm_button")]
    [InlineData("failed")]
    public void Without_any_one_of_its_words_there_is_no_Delete(string missing)
    {
        var d = System.Text.Json.Nodes.JsonNode.Parse(List().GetRawText())!.AsObject();
        d["delete"]!.AsObject().Remove(missing);
        Assert.Null(ChatDeleteWords.Read(JsonDocument.Parse(d.ToJsonString()).RootElement));
        d["delete"]![missing] = " ";
        Assert.Null(ChatDeleteWords.Read(JsonDocument.Parse(d.ToJsonString()).RootElement));
        // An older cloud: no words at all, no Delete, and the list still reads.
        Assert.Null(ChatDeleteWords.Read(JsonDocument.Parse("""{"chats": []}""").RootElement));
    }

    static Task<ChatDeleted> Delete(CloudRead answer, List<string>? asked = null) =>
        CoachChats.DeleteAsync(En, Said(), "taf c", "sess/1", path => { asked?.Add(path); return Task.FromResult(answer); });

    [Fact]
    public async Task Taken_it_leaves_the_lists_and_goes_to_its_own_route()
    {
        var asked = new List<string>();
        Assert.Equal(new ChatDeleted(true, null), await Delete(CloudRead.Of(JsonDocument.Parse("""{"deleted": true}""").RootElement), asked));
        Assert.Equal("/v1/tafs/taf%20c/chats/sess%2F1", Assert.Single(asked));
        // An empty 204 is taken too.
        Assert.Equal(new ChatDeleted(true, null), await Delete(CloudRead.Refused(En, 204, "")));
    }

    [Fact]
    public async Task Not_this_person_s_or_already_gone_leaves_quietly() =>
        Assert.Equal(new ChatDeleted(true, null), await Delete(CloudRead.Refused(En, 404, """{"error": "not found"}""")));

    [Fact]
    public async Task At_work_it_stays_with_the_refusal_s_own_words_else_the_failed_line()
    {
        Assert.Equal(new ChatDeleted(false, "It's still working."), await Delete(CloudRead.Refused(En, 409, """{"error": "It's still working."}""")));
        Assert.Equal(new ChatDeleted(false, Said().Failed), await Delete(CloudRead.Refused(En, 409, "")));
    }

    [Fact]
    public async Task Any_other_failure_the_network_s_included_keeps_it_with_the_failed_line()
    {
        Assert.Equal(new ChatDeleted(false, Said().Failed), await Delete(CloudRead.Unreached(En)));
        Assert.Equal(new ChatDeleted(false, Said().Failed), await Delete(CloudRead.Refused(En, 500, """{"error": "boom"}""")));
    }
}
