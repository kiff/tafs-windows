using System.Text;
using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>A message's own id (client_message_id), so tafs cloud acts on it once however often it arrives.</summary>
[Part("record")]
public class MessageIdTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    static JsonElement Json(byte[] body) => JsonDocument.Parse(body).RootElement;

    [Fact]
    public void Each_message_gets_its_own_UUID()
    {
        var (a, b) = (TafChatSend.NewMessageId(), TafChatSend.NewMessageId());
        Assert.True(Guid.TryParseExact(a, "D", out _));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void The_id_goes_at_the_top_of_every_route_that_takes_it_and_nowhere_without_one()
    {
        const string id = "3f2c6b8e-1d4a-4f0e-9b7a-2c5d8e1f0a9b";
        Assert.Equal("""{"text":"Hi","client_message_id":"3f2c6b8e-1d4a-4f0e-9b7a-2c5d8e1f0a9b"}""", Encoding.UTF8.GetString(TafChatSend.Body("Hi", null, id)));
        Assert.Equal(["f1"], Json(TafChatSend.Body("Hi", ["f1"], id)).GetProperty("attachments").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(id, Json(TafChatSend.Body("Hi", ["f1"], id)).GetProperty("client_message_id").GetString());
        Assert.Equal("""{"text":"Hi"}""", Encoding.UTF8.GetString(TafChatSend.Body("Hi", null)));
        Assert.Equal("""{"run":"r1","text":"Use EUR","hold":true,"client_message_id":"3f2c6b8e-1d4a-4f0e-9b7a-2c5d8e1f0a9b"}""",
            Encoding.UTF8.GetString(TafSteer.Body("r1", "Use EUR", id)));
        Assert.Equal("""{"run":"r1","text":"Use EUR","hold":true}""", Encoding.UTF8.GetString(TafSteer.Body("r1", "Use EUR")));
        Assert.Equal(id, Json(CoachSend.Body("Hi", CoachConversation.New, null, id)).GetProperty("client_message_id").GetString());
        Assert.False(Json(CoachSend.Body("Hi", CoachConversation.New)).TryGetProperty("client_message_id", out _));
    }

    [Fact]
    public async Task A_message_not_sent_keeps_its_id_so_Retry_goes_under_the_same_one()
    {
        var bodies = new List<string>();
        Task<CloudRead> Unreached(string path, byte[] body)
        {
            bodies.Add(Encoding.UTF8.GetString(body));
            return Task.FromResult(new CloudRead(null, "Not reached.", 0));
        }
        var id = TafChatSend.NewMessageId();
        var refused = await TafChatSend.SendAsync(En, "taf_1", "Which refunds are open?", Unreached, messageId: id);
        var unsent = UnsentMessage.Of("Which refunds are open?", refused!.Why, DateTimeOffset.UtcNow, refused.Halted, id);
        await TafChatSend.SendAsync(En, "taf_1", unsent.Text, Unreached, messageId: unsent.MessageId);
        Assert.Equal(2, bodies.Count);
        Assert.All(bodies, b => Assert.Contains(id, b));
    }

    [Fact]
    public async Task A_stream_that_fails_before_its_200_then_the_plain_send_carry_one_id()
    {
        var calls = new List<(string Path, string Body)>();
        Task<CloudRead> Send(string path, byte[] body)
        {
            calls.Add((path, Encoding.UTF8.GetString(body)));
            return Task.FromResult(CloudRead.Of(JsonDocument.Parse("""{"run": "r1"}""").RootElement));
        }
        Task<(int, string, System.Net.Http.HttpResponseMessage?)> NotReached(string path, byte[] body)
        {
            calls.Add((path, Encoding.UTF8.GetString(body)));
            throw new System.Net.Http.HttpRequestException("dropped");
        }
        var id = TafChatSend.NewMessageId();
        var (open, refused) = await TafChatStream.OpenAsync(En, "taf_1", "Which refunds are open?", null, id, NotReached, Send);
        Assert.Null(open);
        Assert.Null(refused);
        Assert.Equal(["/v1/tafs/taf_1/messages/stream", "/v1/tafs/taf_1/messages"], calls.Select(c => c.Path));
        Assert.All(calls, c => Assert.Equal(id, JsonDocument.Parse(c.Body).RootElement.GetProperty("client_message_id").GetString()));

        // No stream route: the same.
        calls.Clear();
        await TafChatStream.OpenAsync(En, "taf_1", "Hi", null, id, (p, b) => { calls.Add((p, Encoding.UTF8.GetString(b))); return Task.FromResult<(int, string, System.Net.Http.HttpResponseMessage?)>((404, "", null)); }, Send);
        Assert.Equal(2, calls.Count);
        Assert.All(calls, c => Assert.Contains(id, c.Body));

        // A refusal is said and never sent again.
        calls.Clear();
        var (_, busy) = await TafChatStream.OpenAsync(En, "taf_1", "Hi", null, id,
            (p, b) => { calls.Add((p, "")); return Task.FromResult<(int, string, System.Net.Http.HttpResponseMessage?)>((503, """{"words": "Try again."}""", null)); }, Send);
        Assert.Single(calls);
        Assert.Equal("Try again.", busy!.Why);
    }

    [Fact]
    public void Another_message_under_an_id_already_used_says_to_send_it_as_a_new_one()
    {
        var conflict = CloudRead.Refused(En, 409, """{"error": "dedup_key_conflict", "code": "dedup_key_conflict"}""");
        Assert.Equal("This message was already sent with different text. Send it again as a new message.", conflict.Why);
        Assert.Equal(CloudRead.DedupKeyConflict, conflict.Code);
        // Another 409 keeps tafs cloud's words.
        Assert.Equal("Two messages already wait.", CloudRead.Refused(En, 409, """{"code": "held_full", "words": "Two messages already wait."}""").Why);
    }

    [Fact]
    public async Task Retry_keeps_the_id_only_when_the_first_send_may_have_landed()
    {
        var id = TafChatSend.NewMessageId();
        async Task<UnsentMessage> NotSent(CloudRead answer) =>
            UnsentMessage.After("Hi", (await TafChatSend.SendAsync(En, "taf_1", "Hi", (_, _) => Task.FromResult(answer), messageId: id))!, DateTimeOffset.UtcNow, id);
        // Not reached, a 503, another 5xx, a timeout: it may have landed, so Retry goes under the same id.
        Assert.Equal(id, (await NotSent(CloudRead.Unreached(En))).MessageId);
        Assert.Equal(id, (await NotSent(CloudRead.Refused(En, 503, """{"words": "Try again."}"""))).MessageId);
        Assert.Equal(id, (await NotSent(CloudRead.Refused(En, 502, ""))).MessageId);
        Assert.Equal(id, (await NotSent(CloudRead.Refused(En, 408, ""))).MessageId);
        // The id is another message's (409 dedup_key_conflict), or any other refusal: Retry is a new message with a new id.
        Assert.Null((await NotSent(CloudRead.Refused(En, 409, """{"code": "dedup_key_conflict"}"""))).MessageId);
        Assert.Null((await NotSent(CloudRead.Refused(En, 400, """{"error": "bad"}"""))).MessageId);
        Assert.Null((await NotSent(CloudRead.Refused(En, 429, """{"words": "Too busy."}"""))).MessageId);

        // Retry after a 409: the body carries a new id, never the refused one.
        var bodies = new List<string>();
        var retry = (await NotSent(CloudRead.Refused(En, 409, """{"code": "dedup_key_conflict"}"""))).MessageId ?? TafChatSend.NewMessageId();
        await TafChatSend.SendAsync(En, "taf_1", "Hi", (_, b) => { bodies.Add(Encoding.UTF8.GetString(b)); return Task.FromResult(CloudRead.Of(JsonDocument.Parse("{}").RootElement)); }, messageId: retry);
        Assert.DoesNotContain(id, Assert.Single(bodies));
    }
}
