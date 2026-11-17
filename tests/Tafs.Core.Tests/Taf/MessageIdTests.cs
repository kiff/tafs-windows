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
}
