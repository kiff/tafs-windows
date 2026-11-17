using System.Text;
using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>A taf's doors (P13), as the iPad's DoorsModel reads and changes them.</summary>
[Part("record")]
public class TafDoorsTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    const string Doors = """
        {"doors": [{"id": "door_1", "app": "email", "kind": "email", "label": "Email", "open": true, "address": "refunds@tafs.example",
                    "proof": "proven", "how": "It checks the sender's server signed the message",
                    "keys": [{"id": "you@company.example", "name": "You", "auto": true}, {"id": "ana@company.example", "name": "Ana"},
                             {"id": "bo@company.example", "name": "bo@company.example"}],
                    "today": 3, "limit": 20, "resting": false, "others_today": 1, "others_limit": 10},
                   {"id": "door_2", "app": "whatsapp", "kind": "whatsapp", "label": "WhatsApp", "open": false, "address": "+33 6 00",
                    "proof": "none", "how": "It can't tell", "keys": [], "today": 20, "limit": 20, "resting": true,
                    "words": {"state": "Closed by you", "tap": "Press to open", "settings": "Its settings are below"}},
                   {"id": "broken"}],
         "apps": [{"id": "email", "label": "Email", "kind": "email", "proof": "proven", "how": "x"},
                  {"id": "slack", "label": "Slack", "kind": "slack", "proof": "proven", "how": "y"}],
         "knocks": [{"id": "k1", "door": "door_1", "app": "email", "from": "mara@customer.example", "name": "Mara", "proof": "pass",
                     "subject": "Refund request", "text": "Hi", "attachments": 2, "at": "2026-11-17T01:00:00Z", "state": "open"},
                    {"id": "k2", "door": "door_1", "app": "email", "from": "x@y", "proof": "fail", "text": "", "at": "2026-11-17T01:00:00Z", "state": "answered"},
                    {"id": "k3", "door": "door_1", "app": "email", "from": "-", "at": "2026-11-17T01:00:00Z", "rest": "others", "dropped": 3}]}
        """;

    [Fact]
    public void A_taf_s_doors_read_with_their_keys_and_knocks()
    {
        var d = TafDoors.Read(JsonDocument.Parse(Doors).RootElement)!;
        Assert.Equal(["door_1", "door_2"], d.Doors.Select(x => x.Id));
        Assert.True(d.Doors[0].HoldsKeys);
        Assert.Equal("Open", TafDoors.State(En, d.Doors[0]));
        Assert.Equal("Closed by you", TafDoors.State(En, d.Doors[1]));
        Assert.Equal(["3 of 20 from you and paired senders today. Text only: attachments aren't opened, and the reply says so.",
            "1 of 10 from anyone else today."], TafDoors.Day(En, d.Doors[0]));
        Assert.Equal(["20 of 20 from you and paired senders today. More are dropped until tomorrow."], TafDoors.Day(En, d.Doors[1]));
        Assert.Equal(("You", "you@company.example"), TafDoors.KeyShown(En, d.Doors[0].Keys[0], "YOU@company.example"));
        Assert.Equal(("You", "you@company.example"), TafDoors.KeyShown(En, d.Doors[0].Keys[0], "you@company.example"));
        Assert.Equal(("you@company.example", null), TafDoors.KeyShown(En, d.Doors[0].Keys[0] with { Name = null }, "someone@else"));
        Assert.Equal(("Ana", "ana@company.example"), TafDoors.KeyShown(En, d.Doors[0].Keys[1], null));
        Assert.Equal(("bo@company.example", null), TafDoors.KeyShown(En, d.Doors[0].Keys[2], null));
        Assert.Equal(["k1"], d.OpenKnocks.Where(k => !k.IsLine).Select(k => k.Id));
        Assert.Equal(["slack"], d.Addable.Select(a => a.Id));
    }

    [Fact]
    public void A_knock_says_who_wrote_and_that_nothing_was_done()
    {
        var d = TafDoors.Read(JsonDocument.Parse(Doors).RootElement)!;
        Assert.Equal(["Mara (mara@customer.example) wrote about \"Refund request\". Nothing was done.", "It had 2 attachments, not opened."],
            TafDoors.KnockLines(En, d.Knocks[0]));
        Assert.Equal(["x@y wrote. Nothing was done.", "It says it's from x@y, but it didn't pass the sender check, so it's treated as a stranger's."],
            TafDoors.KnockLines(En, d.Knocks[1]));
        Assert.Equal(["This door had its messages from people who aren't paired for today. 3 more were dropped."], TafDoors.KnockLines(En, d.Knocks[2]));
    }

    [Fact]
    public void Its_changes_and_their_bodies()
    {
        Assert.Equal("/v1/tafs/t1/doors/door_1/keys/ana%40company.example", TafDoors.KeyPath("t1", "door_1", "ana@company.example").Replace("@", "%40"));
        Assert.Equal("/v1/tafs/t1/doors/knocks/k1", TafDoors.KnockPath("t1", "k1"));
        Assert.Equal("""{"open":false}""", Encoding.UTF8.GetString(TafDoors.OpenBody(false)));
        Assert.Equal("""{"id":"ana@company.example","name":"Ana"}""", Encoding.UTF8.GetString(TafDoors.PairBody(" ana@company.example ", " Ana ")));
        Assert.Equal("""{"id":"ana@company.example"}""", Encoding.UTF8.GetString(TafDoors.PairBody("ana@company.example", " ")));
        Assert.Equal("""{"action":"do","say":"Refund it"}""", Encoding.UTF8.GetString(TafDoors.AnswerBody("do", "Refund it")));
        Assert.Equal("""{"action":"ignore"}""", Encoding.UTF8.GetString(TafDoors.AnswerBody("ignore")));
        Assert.Equal("door_1", TafDoors.Changed(JsonDocument.Parse(Doors).RootElement.GetProperty("doors")[0].GetRawText() is var raw
            ? JsonDocument.Parse("{\"door\":" + raw + "}").RootElement : default)!.Id);
    }
}
