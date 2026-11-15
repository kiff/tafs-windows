using System.Text.Json;
using Tafs.Core.Record;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Record;

/// <summary>The Record from KIFF's activity, as the iPad's KIFFClient.activity and KIFFMapping.record.</summary>
[Part("record")]
public class KiffActivityTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    const string Base = """ "proposal_id": "p1", "action_name": "stripe__create_refund", "outcome": "allowed", "created_at": "2026-11-15T09:30:00.123Z", "held": false, "actor_id": "agent-refunds" """;

    static IReadOnlyList<ActivityCall>? Read(string calls) => KiffActivity.Read(JsonDocument.Parse($$"""{"calls": [{{calls}}]}""").RootElement);

    static RecordEntry Line(string more = "")
    {
        var calls = Read("{" + Base + more + "}")!;
        return Assert.Single(KiffActivity.Lines(En, calls, a => a == "agent-refunds" ? ("Refunds", 4) : null));
    }

    /// <summary>Every field the iPad's ActivityCall declares, read; fields it doesn't know are no matter.</summary>
    [Fact]
    public void A_call_is_read_whole()
    {
        var call = Assert.Single(Read("{" + Base + """
            , "title": "Refund a payment", "tool_outcome": {"status": "done", "message": null}, "amount": {"value": "150.00", "unit": "EUR"},
            "read_only": false, "entity_id": "tc-1", "on_hash": "sha256:ab", "on_arg": "payment",
            "answer": {"decision": "approved", "by": "admin", "you": false, "signed": true, "device": "PC", "at": "2026-11-15T09:31:00+01:00"},
            "receipt": {"trace_id": "t-1", "status": "signed"}, "reasons": ["over the limit"], "made_at": "2026-11-15T09:29:00Z",
            "reason": null, "detail": "x", "new_field": {"a": 1}}
            """)!);

        Assert.Equal(("p1", "stripe__create_refund", "allowed", false), (call.ProposalId, call.ActionName, call.Outcome, call.Held));
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 9, 30, 0, 123, TimeSpan.Zero), call.CreatedAt);
        Assert.Equal(new ToolOutcome("done"), call.Tool);
        Assert.Equal(new CallAmount("150.00", "EUR"), call.Amount);
        Assert.Equal(new CallReceipt("t-1", "signed"), call.Receipt);
        Assert.Equal(("admin", true, new DateTimeOffset(2026, 11, 15, 8, 31, 0, TimeSpan.Zero)), (call.Answer!.By, call.Answer.Signed, call.Answer.At!.Value.ToUniversalTime()));
        Assert.Equal(["over the limit"], call.Reasons!);
        Assert.Equal(("tc-1", "sha256:ab", "payment", "x"), (call.EntityId, call.OnHash, call.OnArg, call.Detail));
    }

    /// <summary>As the iPad's try? on its strict decoder: one call it can't read fails the read, and the Record stays as it was.</summary>
    [Theory]
    [InlineData(""", "held": null""")]
    [InlineData(""", "created_at": "2026-11-15 09:30" """)]
    [InlineData(""", "created_at": "2026-11-15T09:30:00" """)]
    [InlineData(""", "title": 3""")]
    [InlineData(""", "tool_outcome": {"message": "no"}""")]
    [InlineData(""", "amount": {"value": 150}""")]
    [InlineData(""", "answer": {"by": "admin"}""")]
    [InlineData(""", "answer": {"decision": "approved", "at": "yesterday"}""")]
    [InlineData(""", "receipt": {"trace_id": "t-1"}""")]
    [InlineData(""", "reasons": ["a", 1]""")]
    [InlineData(""", "read_only": "no" """)]
    public void A_call_it_can_t_read_fails_the_read(string bad)
    {
        // JSON keeps the last of a repeated name, so each replaces the good one.
        Assert.Null(KiffActivity.Read(JsonDocument.Parse($$"""{"calls": [{{{Base}}}, {{{Base}}{{bad}}}]}""").RootElement));
        Assert.NotNull(Read("{" + Base + "}"));
        Assert.Null(KiffActivity.Read(JsonDocument.Parse("""{"activity": []}""").RootElement));
    }

    /// <summary>As the iPad's executed: done only when the tool said so; each other status in its own words.</summary>
    [Theory]
    [InlineData("""{"status": "done"}""", false, EntryKind.Done, "Done, within its Card")]
    [InlineData("""{"status": "done"}""", true, EntryKind.Approved, "Approved by you, then done")]
    [InlineData("""{"status": "not_sent", "message": "timeout"}""", false, EntryKind.NotDone, "Allowed by its Card, but it couldn't be sent: nothing reached the app: timeout")]
    [InlineData("""{"status": "sending"}""", true, EntryKind.Sending, "Approved by you, and sent: waiting for the app's answer")]
    [InlineData("""{"status": "unknown"}""", false, EntryKind.Unknown, "Allowed by its Card, and sent, but the app's answer was lost: check the app's own records")]
    [InlineData("""{"status": "not_recorded"}""", false, EntryKind.Unknown, "Allowed by its Card, but whether the app did it wasn't recorded: check the app's own records")]
    [InlineData("""{"status": "later"}""", false, EntryKind.Unknown, "Allowed by its Card, but whether the app did it wasn't recorded: check the app's own records")]
    [InlineData("null", false, EntryKind.Unknown, "Allowed by its Card, but whether the app did it wasn't recorded: check the app's own records")]
    [InlineData("""{"status": "refused_by_tool", "message": "card declined"}""", false, EntryKind.NotDone, "The app refused it, and it still counts toward today's limits: card declined")]
    [InlineData("""{"status": "refused_by_tool", "refused_by": "connection", "code": "auth"}""", false, EntryKind.NotDone, "The connection refused it before sending. Nothing was sent.")]
    public void What_the_tool_did_is_its_own(string tool, bool held, EntryKind kind, string detail)
    {
        var line = Line($", \"held\": {(held ? "true" : "false")}, \"tool_outcome\": {tool}");
        Assert.Equal((kind, detail), (line.Kind, line.Detail));
    }

    /// <summary>A refused call that moved money or another unit says it still counts; a read-only one counts nothing.</summary>
    [Theory]
    [InlineData("""{"value": "150.00", "unit": "EUR"}""", "false", "The app refused it. No money moved, but it still counts against today's total.")]
    [InlineData("""{"value": "3", "unit": "seats"}""", "false", "The app refused it. Nothing was done, but it still counts against today's total.")]
    [InlineData("""{"value": "150.00", "unit": "EUR"}""", "true", "The app refused it, and it still counts toward today's limits")]
    public void A_refusal_says_what_still_counts(string amount, string readOnly, string detail) =>
        Assert.Equal(detail, Line($", \"tool_outcome\": {{\"status\": \"refused_by_tool\"}}, \"amount\": {amount}, \"read_only\": {readOnly}").Detail);

    /// <summary>KIFF's decision: held waits for the person; anything else is refused, in KIFF's words when it has them.</summary>
    [Theory]
    [InlineData("owner_required", null, EntryKind.Held, "Waiting for you")]
    [InlineData("approval_required", null, EntryKind.Held, "Waiting for you")]
    [InlineData("denied", "over the €100.00 limit", EntryKind.Refused, "Not sent · over the €100.00 limit")]
    [InlineData("denied", null, EntryKind.Refused, "Not sent")]
    public void What_KIFF_decided(string outcome, string? reason, EntryKind kind, string detail)
    {
        var line = Line($", \"outcome\": \"{outcome}\"" + (reason is null ? "" : $", \"reason\": \"{reason}\""));
        Assert.Equal((kind, detail), (line.Kind, line.Detail));
    }

    /// <summary>
    /// As the iPad's titleWithAmount and amountText: the tool's title (else the action in words) and its money as the app
    /// writes it (a whole amount without decimals, as the iPad's Money.figure); another unit as written; a value that isn't a number, none.
    /// </summary>
    [Theory]
    [InlineData(""", "title": " Refund a payment ", "amount": {"value": "150.00", "unit": "EUR"}""", "Refund a payment · €150", "€150")]
    [InlineData(""", "amount": {"value": "80", "unit": "USD"}""", "create refund · $80", "$80")]
    [InlineData(""", "amount": {"value": "3", "unit": "seats"}""", "create refund · 3 seats", "3 seats")]
    [InlineData(""", "amount": {"value": "30000"}""", "create refund · 30000", "30000")]
    [InlineData(""", "amount": {"value": "lots", "unit": "EUR"}""", "create refund", null)]
    public void A_title_has_its_amount(string more, string title, string? amount)
    {
        var line = Line(more);
        Assert.Equal((title, amount, "Refunds", 4), (line.Title, line.Amount, line.Taf, line.TafColor));
    }

    /// <summary>
    /// An amount that can't be written as the money it is (more than 18 decimals, or more minor units than a whole number
    /// holds) shows none, never a crash on KIFF's answer; 18 decimals are written. A figure made with a scale past 18,
    /// as the Card tab's are, reads "–".
    /// </summary>
    [Theory]
    [InlineData("0.0000000000000000001", null)]
    [InlineData("1.00000000000000000000000000000", null)]
    [InlineData("99999999999.999999999", null)]
    [InlineData("0.000000000000000005", "€0.000000000000000005")]
    public void An_amount_that_can_t_be_written_shows_none(string value, string? text)
    {
        Assert.Equal(text, KiffActivity.AmountText(En, new CallAmount(value, "EUR")));
        Assert.Equal("–", new Tafs.Core.Taf.KiffAmount(5, "EUR", 29).Figure(En));
        Assert.Equal("–", new Tafs.Core.Taf.KiffAmount(5, "EUR", 64).Formatted(En));
    }

    /// <summary>As the iPad's RecordRow.detail: with an answer or a receipt, who decided · what became of it.</summary>
    [Theory]
    [InlineData(""", "held": true, "tool_outcome": {"status": "done"}, "answer": {"decision": "approved", "you": true}""", "approved by you · done")]
    [InlineData(""", "held": true, "tool_outcome": {"status": "done"}, "answer": {"decision": "approved", "by": "admin"}""", "approved by an admin · done")]
    [InlineData(""", "outcome": "denied", "answer": {"decision": "refused", "by": "billing_lead"}""", "refused by someone with the role billing lead · nothing sent")]
    [InlineData(""", "outcome": "denied", "answer": {"decision": "refused"}""", "refused by someone else · nothing sent")]
    [InlineData(""", "tool_outcome": {"status": "done"}, "receipt": {"trace_id": "t", "status": "pending"}""", "within its Card · done")]
    [InlineData(""", "held": true, "tool_outcome": {"status": "sending"}, "receipt": {"trace_id": "t", "status": "pending"}""", "approved by you · sent, no answer yet")]
    [InlineData(""", "outcome": "denied", "receipt": {"trace_id": "t", "status": "signed"}""", "refused by its Card · nothing sent")]
    [InlineData(""", "outcome": "owner_required", "receipt": {"trace_id": "t", "status": "pending"}""", "waiting for you · nothing sent yet")]
    public void Who_decided_and_what_became_of_it(string more, string detail) => Assert.Equal(detail, Line(more).Detail);

    /// <summary>Only the calls of a taf on Home's list, by its agent in KIFF; KIFF's order kept.</summary>
    [Fact]
    public void Only_a_taf_s_calls_are_lines()
    {
        var calls = Read(string.Join(", ", new[] { "agent-refunds", "agent-gone", null, "agent-bills" }.Select((a, i) =>
            $$"""{"proposal_id": "p{{i}}", "action_name": "a{{i}}", "outcome": "allowed", "created_at": "2026-11-15T09:3{{i}}:00Z", "held": false, "actor_id": {{(a is null ? "null" : $"\"{a}\"")}}}""")))!;

        var lines = KiffActivity.Lines(En, calls, a => a switch { "agent-refunds" => ("Refunds", 4), "agent-bills" => ("Bill pay", 3), _ => null });

        Assert.Equal([("Refunds", "a0"), ("Bill pay", "a3")], lines.Select(l => (l.Taf, l.Title)));
    }
}
