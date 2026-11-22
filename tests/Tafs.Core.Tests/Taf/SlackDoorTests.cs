using System.Text;
using System.Text.Json;
using Tafs.Core.Connections;
using Tafs.Core.Kiff;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>A Slack door (tafs docs/doors.md), as the iPad's SlackDoorSheet, addSlackDoor and removeSlackDoor.</summary>
[Part("record")]
public class SlackDoorTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    const string Win = "win_abcdefghijklmnopqrstuvwxyz";
    static readonly SlackChannel Support = new("C042", "support");

    static PlanqueGrant Bot(string id = "grt_s", string status = "connected", string agent = "help-k3", string? account = "acc_s",
        string? entry = SlackDoor.BotEntry, string? toolkit = null) =>
        new(id, status, "Slack", "d", new GrantRequest("slack", agent, [], Toolkit: toolkit), []) { Account = account, Entry = entry };

    sealed class Calls
    {
        public List<string> Said { get; } = [];
        public List<PlanqueGrant> Grants { get; set; } = [Bot()];
        public bool HelloClosed { get; set; }
        public (int, string) Tap { get; set; } = (201, """{"id": "trg_9"}""");
        public CloudRead Door { get; set; } = CloudRead.Of(JsonDocument.Parse("""{"id": "door_1"}""").RootElement);
        public (int, string) Unsubscribed { get; set; } = (204, "");

        public SlackDoorCalls Make() => new(
            new ConnectCalls(
                () => { Said.Add("grants"); return Task.FromResult<IReadOnlyList<PlanqueGrant>?>(Grants); },
                () => { Said.Add("read window"); return Task.FromResult<OpenWindow?>(new OpenWindow(Win, DateTimeOffset.UtcNow.AddMinutes(5), true, "device", null)); },
                () =>
                {
                    Said.Add("hello");
                    if (HelloClosed) throw new OperationCanceledException();
                    return Task.FromResult<(OpenWindow?, string?)>((new OpenWindow(Win, DateTimeOffset.UtcNow.AddMinutes(5), true, "device", null), null));
                },
                (window, path, body) => { Said.Add($"tap {path} {Encoding.UTF8.GetString(body)}"); return Task.FromResult(Tap); },
                (page, grant) => Task.FromResult<string?>(null), () => true, e => e.Message),
            path => { Said.Add($"unsubscribe {path}"); return Task.FromResult(Unsubscribed); },
            body => { Said.Add($"door {Encoding.UTF8.GetString(body)}"); return Task.FromResult(Door); });
    }

    /// <summary>Only the taf's own connected Slack bots: its catalog entry, or with none, its toolkit.</summary>
    [Fact]
    public void A_door_listens_only_through_the_tafs_own_connected_slack_bot()
    {
        var grants = new[]
        {
            Bot("a"), Bot("b", entry: null, toolkit: "slackbot"), Bot("c", status: "pending"), Bot("d", agent: "other"),
            Bot("e", entry: "connector:composio:slack"), Bot("f", entry: null, toolkit: "slack"),
        };
        Assert.Equal(["a", "b"], SlackDoor.Bots(grants, "help-k3").Select(g => g.Id));
        Assert.Empty(SlackDoor.Bots(grants, null));
    }

    [Fact]
    public void Planques_channels_are_read_in_its_order()
    {
        var read = SlackDoor.Channels(JsonDocument.Parse("""
            {"channels": [{"id": "C1", "name": "support"}, {"id": "C2", "private": true, "name": "vip"}, {"id": "C3"}, {"name": "no id"}, "x"]}
            """).RootElement)!;
        Assert.Equal([new SlackChannel("C1", "support"), new SlackChannel("C2", "vip", true), new SlackChannel("C3")], read);
        Assert.Equal(("#support", "C3"), (read[0].Place, read[2].Place));
        Assert.Null(SlackDoor.Channels(JsonDocument.Parse("""{"error": "x"}""").RootElement));
        Assert.Equal("/v1/accounts/t1/grants/grt%2Fs/channels", SlackDoor.ChannelsPath("t1", "grt/s"));
    }

    /// <summary>The agreed order: the connection read again, Windows Hello, planque's subscription of that one channel, then the door.</summary>
    [Fact]
    public async Task Planque_subscribes_the_channel_behind_windows_hello_then_tafs_cloud_makes_the_door()
    {
        var calls = new Calls();
        var result = await SlackDoor.AddAsync(En, "t1", "help-k3", "slack", "grt_s", Support, calls.Make());

        Assert.Equal(new SlackDoorResult(true), result);
        Assert.Equal(
        [
            "grants", "hello",
            """tap /v1/accounts/t1/grants/grt_s/triggers {"account":"acc_s","slug":"SLACKBOT_CHANNEL_MESSAGE_RECEIVED","channels":["C042"]}""",
            """door {"app":"slack","grant":"grt_s","channel":"C042","trigger":"trg_9"}""",
        ], calls.Said);
    }

    [Fact]
    public async Task Windows_hello_closed_subscribes_nothing()
    {
        var calls = new Calls { HelloClosed = true };
        Assert.Equal(new SlackDoorResult(false), await SlackDoor.AddAsync(En, "t1", "help-k3", "slack", "grt_s", Support, calls.Make()));
        Assert.DoesNotContain(calls.Said, s => s.StartsWith("tap") || s.StartsWith("door"));
    }

    /// <summary>A connection that's no longer the taf's, connected or ready, asks nothing and subscribes nothing.</summary>
    [Theory]
    [InlineData("pending", "help-k3", "acc_s", "Connect the Slack bot for this taf first")]
    [InlineData("connected", "other", "acc_s", "Connect the Slack bot for this taf first")]
    [InlineData("connected", "help-k3", null, "This Slack connection isn't ready yet")]
    public async Task A_connection_that_changed_since_asks_nothing(string status, string agent, string? account, string says)
    {
        var calls = new Calls { Grants = [Bot(status: status, agent: agent, account: account)] };
        var result = await SlackDoor.AddAsync(En, "t1", "help-k3", "slack", "grt_s", Support, calls.Make());
        Assert.False(result.Added);
        Assert.StartsWith(says, result.Why);
        Assert.Equal(["grants"], calls.Said);
    }

    [Fact]
    public async Task A_subscription_planque_refuses_makes_no_door()
    {
        var calls = new Calls { Tap = (403, """{"message": "That channel is private."}""") };
        var result = await SlackDoor.AddAsync(En, "t1", "help-k3", "slack", "grt_s", Support, calls.Make());
        Assert.Equal(new SlackDoorResult(false, "Slack didn't subscribe #support: That channel is private."), result);
        Assert.DoesNotContain(calls.Said, s => s.StartsWith("door"));

        calls = new Calls { Tap = (201, "{}") };
        Assert.False((await SlackDoor.AddAsync(En, "t1", "help-k3", "slack", "grt_s", Support, calls.Make())).Added);
        Assert.DoesNotContain(calls.Said, s => s.StartsWith("door"));
    }

    /// <summary>Nothing listens without a door: one tafs cloud refuses takes the subscription back.</summary>
    [Fact]
    public async Task A_door_tafs_cloud_refuses_takes_the_subscription_back()
    {
        var calls = new Calls { Door = new CloudRead(null, "This taf has a Slack door already.", 409) };
        var result = await SlackDoor.AddAsync(En, "t1", "help-k3", "slack", "grt_s", Support, calls.Make());
        Assert.Equal(new SlackDoorResult(false, "The Slack door wasn't added. This taf has a Slack door already."), result);
        Assert.Equal("unsubscribe /v1/accounts/t1/grants/grt_s/triggers/trg_9", calls.Said[^1]);
    }

    static Door SlackDoorOf(string? grant, string? trigger, string kind = "slack") =>
        new("door_1", "slack", kind, "Slack", true, "#support", "proven", "Slack member", [], 0, 50, false, Grant: grant, Trigger: trigger);

    /// <summary>Removing: planque's subscription stops first; one it doesn't stop keeps its door.</summary>
    [Fact]
    public async Task Removing_stops_the_subscription_first()
    {
        var asked = new List<string>();
        Task<(int, string)> Answer((int, string) a, string path) { asked.Add(path); return Task.FromResult(a); }

        Assert.Null(await SlackDoor.StopAsync(En, "t1", SlackDoorOf("grt_s", "trg_9"), p => Answer((204, ""), p)));
        Assert.Equal(["/v1/accounts/t1/grants/grt_s/triggers/trg_9"], asked);
        // Gone at planque already is stopped.
        Assert.Null(await SlackDoor.StopAsync(En, "t1", SlackDoorOf("grt_s", "trg_9"), p => Answer((404, ""), p)));

        Assert.Equal("Slack's subscription wasn't stopped, so the door stays: It's still in use.",
            await SlackDoor.StopAsync(En, "t1", SlackDoorOf("grt_s", "trg_9"), p => Answer((409, """{"error": "It's still in use."}"""), p)));
        Assert.StartsWith("Slack's subscription wasn't stopped, so the door stays: ",
            await SlackDoor.StopAsync(En, "t1", SlackDoorOf("grt_s", "trg_9"), p => Answer((0, ""), p)));

        // A door that names no subscription, or isn't Slack's, is only the door.
        asked.Clear();
        Assert.Null(await SlackDoor.StopAsync(En, "t1", SlackDoorOf("grt_s", null), p => Answer((500, ""), p)));
        Assert.Null(await SlackDoor.StopAsync(En, "t1", SlackDoorOf("grt_s", "trg_9", kind: "email"), p => Answer((500, ""), p)));
        Assert.Empty(asked);
    }

    /// <summary>tafs cloud's door names its connection and subscription.</summary>
    [Fact]
    public void A_door_reads_its_grant_and_trigger()
    {
        var read = TafDoors.Read(JsonDocument.Parse("""
            {"doors": [{"id": "door_1", "app": "slack", "kind": "slack", "label": "Slack", "open": true, "address": "#support", "proof": "proven",
                        "how": "Slack member", "today": 0, "limit": 50, "grant": "grt_s", "trigger": "trg_9"}], "addable": [], "knocks": []}
            """).RootElement);
        Assert.Equal(("grt_s", "trg_9"), (read!.Doors[0].Grant, read.Doors[0].Trigger));
    }
}
