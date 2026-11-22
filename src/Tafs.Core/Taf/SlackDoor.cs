using System.Text;
using System.Text.Json;
using Tafs.Core.Connections;
using Tafs.Core.Kiff;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>A channel a Slack bot connection can see, as planque lists it (the iPad's <c>PlanqueClient.Channel</c>).</summary>
public sealed record SlackChannel(string Id, string Name = "", bool Private = false)
{
    /// <summary>Where it is, as the person reads it: "#support", or its id when Slack gave no name.</summary>
    public string Place => Name.Length == 0 ? Id : "#" + Name;
}

/// <summary>
/// What a Slack door needs (the iPad's <c>addSlackDoor</c>): planque's calls for the connection, its grants and the window
/// Windows Hello opens; taking a subscription back (<c>DELETE …/triggers/{id}</c>, signed by this PC's key, which asks no one);
/// and tafs cloud making the door.
/// </summary>
public sealed record SlackDoorCalls(ConnectCalls Planque, Func<string, Task<(int Status, string Body)>> Unsubscribe, Func<byte[], Task<CloudRead>> AddDoor);

/// <summary>What adding came to: added, or why not. Neither: Windows Hello closed, or another session since.</summary>
public sealed record SlackDoorResult(bool Added, string? Why = null);

/// <summary>
/// A Slack door (tafs docs/doors.md, the iPad's <c>SlackDoorSheet</c>): one of the taf's Slack bot connections, then one of its
/// channels. In the agreed order, planque subscribes the channel to Slack's message event inside a window opened with
/// Windows Hello, then tafs cloud makes the door; a door that can't be made takes the subscription back, so nothing listens
/// without a door. Removing goes the other way: the subscription first, then the door.
/// </summary>
public static class SlackDoor
{
    /// <summary>The event a Slack door listens to.</summary>
    public const string MessageEvent = "SLACKBOT_CHANNEL_MESSAGE_RECEIVED";

    /// <summary>planque's catalog entry for the Slack bot connection.</summary>
    public const string BotEntry = "connector:composio:slackbot";

    /// <summary>A Slack bot connection, as the iPad's <c>Grant.slackBot</c>: its catalog entry, or with none, its toolkit.</summary>
    public static bool IsBot(PlanqueGrant g) => g.Entry == BotEntry || (g.Entry is null && g.Request.Toolkit == "slackbot");

    /// <summary>The taf's Slack bot connections a door can listen through: connected, and its own agent's. None without an agent.</summary>
    public static IReadOnlyList<PlanqueGrant> Bots(IEnumerable<PlanqueGrant> grants, string? agent) =>
        agent is null ? [] : grants.Where(g => IsBot(g) && g.Connected && g.Request.Agent == agent).ToList();

    /// <summary><c>GET …/grants/{id}/channels</c>: planque's <c>signedSafe</c> read, signed by this PC's key.</summary>
    public static string ChannelsPath(string tenant, string grant) => PlanqueProof.GrantsPath(tenant) + "/" + CloudRead.PathPart(grant) + "/channels";

    /// <summary>planque's channels, <c>{channels: [{id, name?, private?}]}</c>, in its order: none when the answer isn't one.</summary>
    public static IReadOnlyList<SlackChannel>? Channels(JsonElement answer)
    {
        if (Lenient.Field(answer, "channels") is not { ValueKind: JsonValueKind.Array } list) return null;
        return list.EnumerateArray()
            .Where(c => c.ValueKind == JsonValueKind.Object && Lenient.Text(c, "id") is { Length: > 0 })
            .Select(c => new SlackChannel(Lenient.Text(c, "id")!, Lenient.Text(c, "name") ?? "", Lenient.Bool(c, "private")))
            .ToList();
    }

    /// <summary>What planque subscribes: the connection's account, Slack's message event and the one channel.</summary>
    public static byte[] SubscribeBody(string account, string channel) =>
        Encoding.UTF8.GetBytes("{\"account\":" + JsonSerializer.Serialize(account) + ",\"slug\":" + JsonSerializer.Serialize(MessageEvent)
            + ",\"channels\":[" + JsonSerializer.Serialize(channel) + "]}");

    /// <summary><c>DELETE …/grants/{grant}/triggers/{trigger}</c>: planque's <c>signedSafe</c> route.</summary>
    public static string UnsubscribePath(string tenant, string grant, string trigger) =>
        KnobEvent.TriggersPath(tenant, grant) + "/" + CloudRead.PathPart(trigger);

    /// <summary>The door tafs cloud makes (<c>POST /v1/tafs/{id}/doors</c>): its app, the connection, the channel and planque's subscription.</summary>
    public static byte[] DoorBody(string app, string grant, string channel, string trigger) =>
        JsonSerializer.SerializeToUtf8Bytes(new { app, grant, channel, trigger });

    /// <summary>
    /// Adds the door: the connection read again (still this taf's, connected, with its account), Windows Hello each time as the
    /// iPad asks Face ID, planque's subscription, then tafs cloud's door. A door tafs cloud refuses takes the subscription back.
    /// </summary>
    public static async Task<SlackDoorResult> AddAsync(WordBook w, string tenant, string? agent, string app, string grantId, SlackChannel channel,
        SlackDoorCalls calls)
    {
        var none = new SlackDoorResult(false);
        var planque = calls.Planque;
        var grants = await planque.Grants();
        if (!planque.Current()) return none;
        if (grants is null) return new(false, w.Get("error.offline"));
        if (Bots(grants, agent).FirstOrDefault(g => g.Id == grantId) is not { } grant)
            return new(false, w.Get("door.slack.connect_first_in_connect_apps", ("taf", w.Get("door.slack.this_taf"))));
        if (grant.Account is not { Length: > 0 } account) return new(false, w.Get("door.slack.no_account"));

        OpenWindow? window;
        // Windows Hello closed says nothing, as approving an app event.
        try { window = await ConnectFlow.WindowAsync(planque, fresh: true); }
        catch (OperationCanceledException) { return none; }
        catch (Exception e) { return new(false, planque.Why(e)); }
        if (window is null || !planque.Current()) return none;

        string trigger;
        try
        {
            var (status, body, _) = await ConnectFlow.TapAsync(planque, window, KnobEvent.TriggersPath(tenant, grant.Id), SubscribeBody(account, channel.Id));
            if (!planque.Current()) return none;
            if (status is < 200 or > 299) return new(false, w.Get("door.slack_did_not_subscribe", ("where_", channel.Place), ("why", Said(w, status, body))));
            if (KnobEvent.Subscription(body) is not { } made) return new(false, w.Get("setup.knob.no_subscription"));
            trigger = made;
        }
        catch (OperationCanceledException) { return none; }
        catch (Exception e) { return new(false, w.Get("door.slack_did_not_subscribe", ("where_", channel.Place), ("why", planque.Why(e)))); }

        var door = await calls.AddDoor(DoorBody(app, grant.Id, channel.Id, trigger));
        if (door.Answer is not null) return new(true);
        // Nothing listens without a door: the subscription goes back, as far as planque takes it.
        try { await calls.Unsubscribe(UnsubscribePath(tenant, grant.Id, trigger)); }
        catch (Exception) { }
        return new(false, w.Get("door.slack_not_added", ("why", door.Why ?? w.Get("error.offline"))));
    }

    /// <summary>
    /// Removing a Slack door, as the iPad's <c>removeSlackDoor</c>: planque's subscription first, then the door. Why the door
    /// stays when planque didn't stop it; none when it did, or when the door names no subscription (then it's only the door).
    /// </summary>
    public static async Task<string?> StopAsync(WordBook w, string tenant, Door door, Func<string, Task<(int Status, string Body)>> unsubscribe)
    {
        if (door.Kind != "slack" || door.Grant is not { Length: > 0 } grant || door.Trigger is not { Length: > 0 } trigger) return null;
        int status;
        string body;
        try { (status, body) = await unsubscribe(UnsubscribePath(tenant, grant, trigger)); }
        catch (Exception) { return w.Get("door.slack_subscription_not_stopped_door", ("why", w.Get("error.offline"))); }
        // Gone already is stopped: planque has nothing listening there.
        if (status is >= 200 and <= 299 or 404) return null;
        return w.Get("door.slack_subscription_not_stopped_door", ("why", status == 0 ? w.Get("error.offline") : Said(w, status, body)));
    }

    /// <summary>planque's own words for a refusal, else the status.</summary>
    static string Said(WordBook w, int status, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if ((Lenient.Text(doc.RootElement, "message") ?? Lenient.Text(doc.RootElement, "error")) is { Length: > 0 } said) return said;
        }
        catch (JsonException) { }
        return w.Get("error.kiff_answered", ("status", status.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
}
