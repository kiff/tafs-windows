using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tafs.Core.NeedsYou;
using Tafs.Core.Record;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Record;

/// <summary>
/// The Record's last pieces, as the iPad's: tafs' own events beside KIFF's calls (inTafs, eventWords), what a line acted on
/// (onText, KIFFMapping.naming, kiff/tafs#167), and a hold KIFF no longer holds (isWaiting).
/// </summary>
[Part("record")]
public class RecordEventsTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    static readonly IReadOnlyList<(string Id, string Name, int Colour)> Tafs = [("t1", "Refunds", 3), ("t2", "Support", 5)];

    static IReadOnlyList<TafsEvent> Events(string json) => TafsEvents.Read(JsonDocument.Parse(json).RootElement)!;

    [Fact]
    public void Tafs_events_are_read_and_one_without_a_time_or_kind_is_left_out()
    {
        var events = Events("""
            {"events": [{"at": "2026-11-22T09:00:00Z", "kind": "halted", "taf": "t1"}, {"at": "2026-11-22T10:00:00Z", "kind": "resumed"},
                        {"kind": "halted"}, {"at": "yesterday", "kind": "halted"}, {"at": "2026-11-22T11:00:00Z"}, "x"]}
            """);
        Assert.Equal([new TafsEvent(new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.Zero), "halted", "t1"),
            new TafsEvent(new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero), "resumed")], events);
        Assert.Null(TafsEvents.Read(JsonDocument.Parse("{}").RootElement));
    }

    /// <summary>One line a taf, or one for each when it was all of them; a taf not listed, or a kind not known, is none.</summary>
    [Fact]
    public void An_event_is_a_line_for_its_taf_or_for_each_taf_when_it_was_all_of_them()
    {
        var lines = TafsEvents.Lines(En, Events("""
            {"events": [{"at": "2026-11-22T09:00:00Z", "kind": "halted", "taf": "t1"},
                        {"at": "2026-11-22T10:00:00Z", "kind": "resumed"},
                        {"at": "2026-11-22T11:00:00Z", "kind": "halted", "taf": "gone"},
                        {"at": "2026-11-22T12:00:00Z", "kind": "something_new", "taf": "t1"}]}
            """), Tafs);
        Assert.Equal([("Refunds", "Halted"), ("Refunds", "Resumed with all your tafs"), ("Support", "Resumed with all your tafs")],
            lines.Select(l => (l.Taf, l.Title)));
        Assert.All(lines, l => Assert.Equal(EntryKind.InTafs, l.Kind));
        Assert.All(lines, l => Assert.Null(l.Call));
        Assert.Equal(Dot.Quiet, RecordScreen.DotOf(EntryKind.InTafs));
        Assert.Equal("Nothing of it runs until you resume it", lines[0].Detail);
    }

    [Fact]
    public void A_knock_says_who_and_by_which_door()
    {
        TafsEvent E(string kind, string? app = "whatsapp", string? from = "Ana") => new(DateTimeOffset.UtcNow, kind, "t1", app, from);
        Assert.Equal(("Ana wrote by WhatsApp", "Not paired, so nothing was done"), TafsEvents.Words(En, E("knocked"))!.Value);
        Assert.Equal("By email, in your words", TafsEvents.Words(En, E("knock_done", "email"))!.Value.Detail);
        Assert.Equal(("You paired Someone", "Their messages by a door give it work from now on"), TafsEvents.Words(En, E("paired", null, null))!.Value);
        Assert.Equal("You removed its Slack door", TafsEvents.Words(En, E("door_removed", "slack"))!.Value.Title);
        Assert.Null(TafsEvents.Words(En, E("unknown_kind")));
    }

    /// <summary>tafs' events only under All: Money moved, Stopped and Waiting are KIFF's calls.</summary>
    [Fact]
    public void An_event_shows_only_under_all()
    {
        var line = new RecordEntry("Refunds", 3, EntryKind.InTafs, "Halted", "Nothing runs", DateTimeOffset.UtcNow);
        Assert.True(RecordScreen.Keeps(RecordFilter.All, line));
        Assert.False(RecordScreen.Keeps(RecordFilter.MoneyMoved, line));
        Assert.False(RecordScreen.Keeps(RecordFilter.Stopped, line));
        Assert.False(RecordScreen.Keeps(RecordFilter.Waiting, line));
    }

    [Fact]
    public void Calls_and_events_are_one_record_newest_first()
    {
        var at = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
        RecordEntry L(string t, int h, EntryKind k) => new("Refunds", 3, k, t, "", at.AddHours(h));
        var record = TafsEvents.With([L("call 1", -1, EntryKind.Done), L("call 3", -3, EntryKind.Done)], [L("halt", -2, EntryKind.InTafs)]);
        Assert.Equal(["call 1", "halt", "call 3"], record.Select(e => e.Title));
    }

    static string Hash(string v) => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(v))).ToLowerInvariant();

    static RecordEntry Line(string? entity = "inv_1", string? hash = null, string? arg = "payment", EntryKind kind = EntryKind.Done, string proposal = "p1") =>
        new("Refunds", 3, kind, "Refund a payment · €150.00", "Done, within its Card", DateTimeOffset.UtcNow, "€150.00",
            new ActivityCall(proposal, "stripe__create_refund", "allowed", DateTimeOffset.UtcNow, false, ActorId: "agent-refunds",
                EntityId: entity, OnHash: hash ?? Hash("pi_3QbstR"), OnArg: arg));

    /// <summary>Only a value whose hash is KIFF's names a line: "payment …QbstR", never a value KIFF didn't hash.</summary>
    [Fact]
    public void A_line_names_what_it_acted_on_only_when_the_value_is_what_kiff_hashed()
    {
        Assert.True(RecordNaming.IsOn("pi_3QbstR", Hash("pi_3QbstR")));
        Assert.False(RecordNaming.IsOn("pi_3QbstR ", Hash("pi_3QbstR")), "untrimmed, as KIFF hashed it");
        Assert.False(RecordNaming.IsOn("PI_3QBSTR", Hash("pi_3QbstR")), "in its own case");

        var refs = RecordNaming.Refs(JsonDocument.Parse("""{"calls": [{"invocation": "inv_1", "value": "pi_3QbstR"}, {"invocation": "inv_2"}]}""").RootElement)!;
        Assert.Equal(["inv_1"], refs.Keys);
        Assert.Equal("/v1/tafs/t%2F1/calls", RecordNaming.CallsPath("t/1"));

        var named = RecordNaming.Named([Line(), Line(hash: Hash("something else")), Line(entity: null)], refs);
        Assert.Equal(["pi_3QbstR", null, null], named.Select(e => e.OnValue));
        Assert.True(RecordNaming.Unnamed(named[1]));
        Assert.False(RecordNaming.Unnamed(named[0]));

        Assert.Equal("payment …QbstR", RecordNaming.OnText(En, named[0]));
        Assert.Equal("customer cus_1", RecordNaming.OnText(En, Line(arg: "customer") with { OnValue = "cus_1" }));
        Assert.Equal("it cus_1", RecordNaming.OnText(En, Line(arg: null) with { OnValue = "cus_1" }));
        Assert.Equal("payment …QbstR · Done, within its Card", RecordNaming.Detail(En, named[0], []));
    }

    /// <summary>A held line KIFF no longer holds (it expired, or its run stopped) says so; before the holds are read, it still waits.</summary>
    [Fact]
    public void A_hold_kiff_dropped_says_no_longer_waiting()
    {
        var line = Line(kind: EntryKind.Held, proposal: "p7") with { Detail = "waiting for you" };
        HeldCall Hold(string? proposal, string? kiff = "h1") =>
            new("Refunds", 3, "Refund", null, "", "", DateTimeOffset.UtcNow.AddMinutes(5), []) { ProposalId = proposal, KiffId = kiff };

        Assert.Equal("waiting for you", RecordNaming.Detail(En, line, null));
        Assert.Equal("waiting for you", RecordNaming.Detail(En, line, [Hold("p7")]));
        Assert.Equal("waiting for you", RecordNaming.Detail(En, line, [Hold(null, kiff: "p7")]));
        Assert.Equal("no longer waiting · nothing sent", RecordNaming.Detail(En, line, [Hold("p8")]));
        Assert.Equal("no longer waiting · nothing sent", RecordNaming.Detail(En, line, []));
        // Only a held line: a done one keeps its words whatever KIFF holds.
        Assert.Equal("Done, within its Card", RecordNaming.Detail(En, Line(), []));
    }
}
