using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tafs.Core.Approvals;
using Tafs.Core.Connections;
using Tafs.Core.Kiff;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>
/// What approving an app event needs from this PC (P4): tafs cloud's dial read again, planque's calls inside KIFF's approval
/// window (the grants, the window opened with Windows Hello, a tap signed by this PC's key), and tafs cloud's record of the
/// subscription.
/// </summary>
public sealed record KnobEventCalls(Func<Task<CloudRead>> ReadKnob, ConnectCalls Planque, Func<byte[], Task<CloudRead>> Record);

/// <summary>A subscription planque made that tafs cloud hasn't recorded yet: tried again as it is, never made twice.</summary>
public sealed record KnobEventReceipt(int Version, KnobApproval Approval, string Subscription);

/// <summary>What approving came to: approved, or why not, with the dial as tafs cloud last said. All empty: Windows Hello closed.</summary>
public sealed record KnobEventResult(bool Approved, string? Why = null, TafKnob? Knob = null, KnobEventReceipt? Kept = null);

/// <summary>
/// An app event's filters, read and approved before planque subscribes (P4), as the iPad's <c>AppEventApprovalSheet</c> and
/// <c>approveKnobEvent</c>: the person reads every filter tafs cloud wrote; Approve reads the dial again, and anything that
/// changed since (its version, grant, event or filters) signs nothing; then, in a window opened with Windows Hello each time,
/// planque subscribes those exact filters (<c>POST …/grants/{grant}/triggers</c>, signed by this PC's key), and tafs cloud
/// records the subscription (<c>POST /v1/tafs/{id}/knob/subscriptions</c>).
/// </summary>
public static class KnobEvent
{
    /// <summary>Whether this stop asks to be approved now: an app event whose approval is needed, offered as Approve.</summary>
    public static bool Asks(KnobItem item) => item is { Kind: "app_event", Ok: "approve", Approval.State: "needed" };

    /// <summary>
    /// The filters as the sheet shows them, as the iPad's encoder writes them: indented, keys sorted, slashes as they are.
    /// Nothing is left out or added. A character that can't be seen (a format character such as a right-to-left override or a
    /// zero-width space, or a line or paragraph separator) shows as its <c>\uXXXX</c>, so no value reads as another. None for
    /// filters that repeat a key: what's shown and what planque keeps could differ, so nothing is shown or signed.
    /// </summary>
    public static string? Filters(KnobApproval approval)
    {
        if (approval.Config is not { } config) return "";
        if (!Readable(config)) return null;
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        return Visible(Sorted(JsonNode.Parse(config.GetRawText()))?.ToJsonString(options) ?? "");
    }

    /// <summary>Filters that say each thing once: no object, at any depth, repeats a key.</summary>
    public static bool Readable(JsonElement config) => config.ValueKind switch
    {
        JsonValueKind.Object => config.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == config.EnumerateObject().Count()
            && config.EnumerateObject().All(p => Readable(p.Value)),
        JsonValueKind.Array => config.EnumerateArray().All(Readable),
        _ => true,
    };

    static string Visible(string shown)
    {
        var said = new StringBuilder(shown.Length);
        foreach (var c in shown)
        {
            var hidden = char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Format
                or System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator;
            if (hidden) said.Append("\\u").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
            else said.Append(c);
        }
        return said.ToString();
    }

    static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value)))),
        JsonArray a => new JsonArray(a.Select(Sorted).ToArray()),
        null => null,
        _ => node.DeepClone(),
    };

    /// <summary>The same approval: its grant, its event, and its filters, compared canonically (JCS), as the iPad compares them.</summary>
    public static bool Same(KnobApproval a, KnobApproval b) =>
        a.Grant == b.Grant && a.Event == b.Event && Canonical(a.Config).SequenceEqual(Canonical(b.Config));

    static byte[] Canonical(JsonElement? config) =>
        config is { } c ? Jcs.Canonical(Encoding.UTF8.GetBytes(c.GetRawText())) : [];

    public static string TriggersPath(string tenant, string grant) => PlanqueProof.GrantsPath(tenant) + "/" + CloudRead.PathPart(grant) + "/triggers";

    /// <summary>planque's body: the connection's account, the event, and tafs cloud's filters as written, in one signed request.</summary>
    public static byte[] SubscribeBody(string account, string slug, JsonElement config) =>
        Encoding.UTF8.GetBytes("{\"account\":" + JsonSerializer.Serialize(account) + ",\"slug\":" + JsonSerializer.Serialize(slug)
            + ",\"config\":" + config.GetRawText() + "}");

    public static string RecordPath(string taf) => TafKnob.Path(taf) + "/subscriptions";

    public static byte[] RecordBody(string trigger, string subscription, int version) =>
        JsonSerializer.SerializeToUtf8Bytes(new { trigger, subscription, version });

    /// <summary>planque's answer: the subscription's id (<c>id</c>, else <c>trigger</c>); none when it says neither.</summary>
    public static string? Subscription(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return (Lenient.Text(doc.RootElement, "id") ?? Lenient.Text(doc.RootElement, "trigger")) is { Length: > 0 } id ? id : null;
        }
        catch (JsonException) { return null; }
    }

    /// <param name="shown">The stop as the sheet showed it, filters and all.</param>
    /// <param name="version">The dial's version the sheet was read at.</param>
    /// <param name="agent">The taf's agent in KIFF: only a connection made for it subscribes.</param>
    /// <param name="kept">A subscription planque already made for this stop that tafs cloud didn't record.</param>
    public static async Task<KnobEventResult> ApproveAsync(WordBook w, string tenant, string? agent, int version, KnobItem shown,
        KnobEventCalls calls, KnobEventReceipt? kept)
    {
        var none = new KnobEventResult(false, Kept: kept);
        var planque = calls.Planque;
        if (!Asks(shown) || shown.Approval is not { } approval || approval.Config is not { } config) return none;
        // Filters that repeat a key read one way here and another at planque: nothing is signed.
        if (!Readable(config)) return new(false, w.Get("knob.event_changed_read_dial_again"), Kept: kept);
        // Only this taf's own connection subscribes: with no agent to match, none does.
        if (agent is null) return new(false, w.Get("knob.connect_first"), Kept: kept);

        // The dial read again before anything is asked: a draft or a stale sheet never subscribes other filters.
        var read = await calls.ReadKnob();
        if (!planque.Current()) return none;
        if (read.Answer is not { } answer || TafKnob.Read(answer) is not { } live)
            return none with { Why = read.Why ?? w.Get("error.offline") };
        var current = live.Wakes.FirstOrDefault(i => i.Id == shown.Id);
        if (live.Version != version || current?.Approval is not { Config: { } nowConfig } now || !Readable(nowConfig) || !Same(now, approval))
            return new(false, w.Get("knob.event_changed_read_dial_again"), live, kept);
        if (now.State == "approved") return new(true, Knob: live);
        if (now.State != "needed" || current.Ok != "approve") return none with { Knob = live };

        var receipt = kept is { } k && k.Version == version && Same(k.Approval, approval) ? k : null;
        try
        {
            if (receipt is null)
            {
                var grants = await planque.Grants();
                if (!planque.Current()) return none;
                if (grants?.FirstOrDefault(g => g.Id == approval.Grant && g.Connected && g.Request.Agent == agent)
                    is not { Account: { Length: > 0 } account })
                    return new(false, w.Get("knob.connect_first"), live, kept);
                // Windows Hello each time, as the iPad asks Face ID for each subscription: a window already open isn't enough.
                if (await ConnectFlow.WindowAsync(planque, fresh: true) is not { } window) return none;
                var (status, body, _) = await ConnectFlow.TapAsync(planque, window, TriggersPath(tenant, approval.Grant),
                    SubscribeBody(account, approval.Event, config));
                if (!planque.Current()) return none;
                if (status is < 200 or > 299) return new(false, Refused(w, status, body), live, kept);
                if (Subscription(body) is not { } made) return new(false, w.Get("setup.knob.no_subscription"), live, kept);
                receipt = new(version, approval, made);
            }

            var recorded = await calls.Record(RecordBody(shown.Id, receipt.Subscription, receipt.Version));
            if (!planque.Current()) return none;
            var after = recorded.Answer is { } r ? TafKnob.Read(r) : null;
            if (after?.Wakes.FirstOrDefault(i => i.Id == shown.Id)?.Approval is not { State: "approved" } done || done.Subscription != receipt.Subscription)
                return new(false, recorded.Answer is null && recorded.Why is { } why ? why : w.Get("knob.subscription_made_dial_has_not"), after ?? live, receipt);
            return new(true, Knob: after);
        }
        catch (ConnectFlow.WindowRefused refused) { return planque.Current() ? new(false, refused.Message, live, receipt ?? kept) : none; }
        catch (OperationCanceledException) { return none with { Kept = receipt ?? kept }; }
        catch (Exception e) when (e is HttpRequestException or PasskeyRefusedException)
        {
            return planque.Current() ? new(false, planque.Why(e), live, receipt ?? kept) : none;
        }
    }

    /// <summary>planque's refusal in its words (<c>message</c>, else <c>error</c>), else its status.</summary>
    static string Refused(WordBook w, int status, string body)
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
