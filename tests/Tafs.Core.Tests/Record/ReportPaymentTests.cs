using System.Text.Json;
using Tafs.Core.Record;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Record;

/// <summary>"Report this payment" (tafs docs/report-payment.md), as the iPad's ReportPaymentSheet and ReportPaymentTests.</summary>
[Part("record")]
public class ReportPaymentTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    const string Call = """
        "call": {"proposal_id": "p1", "actor_id": "agent-refunds", "action_name": "stripe__create_refund", "title": "Refund a payment",
                 "outcome": "allowed", "created_at": "2026-11-15T09:30:00.000Z", "held": false, "amount": {"value": "150.00", "unit": "EUR"},
                 "tool_outcome": {"status": "done"}, "receipt": {"trace_id": "t-1", "status": "signed"}}
        """;

    /// <summary>One of tafs cloud's Activity entries, as a taf's Activity, the Record's search and a case read them.</summary>
    static ActivityLine Line(string more) =>
        TafActivity.Line(JsonDocument.Parse($$"""
            {"id": "e1", "kind": "done", "title": "Refund a payment", "detail": "Done, within its Card", "time": "09:30",
             "amount_text": "€150.00"{{more}}}
            """).RootElement)!;

    /// <summary>A payment this reader may report: the call opens its receipt, and the receipt offers the report.</summary>
    [Fact]
    public void An_offered_payment_opens_its_receipt_with_the_report()
    {
        var line = Line(", " + Call + """, "report": {"trace_id": "t-1", "at": "2026-11-15T09:30:00.000Z"}""");
        Assert.Equal(new ReportOffer("t-1", new DateTimeOffset(2026, 11, 15, 9, 30, 0, TimeSpan.Zero)), line.Report);
        Assert.Null(line.ReportedAt);

        var entry = ReportPayment.Evidence(line, "Refunds", 4)!;
        // tafs cloud's kind, title, detail and amount, as sent; KIFF's call for the receipt.
        Assert.Equal((EntryKind.Done, "Refund a payment", "Done, within its Card", "€150.00"), (entry.Kind, entry.Title, entry.Detail, entry.Amount));
        Assert.Equal(("Refunds", "t-1"), (entry.Taf, entry.Call!.Receipt!.TraceId));
        Assert.True(ReportPayment.Offers(entry));
        Assert.False(ReportPayment.Offers(entry, reportedNow: true), "reported from this sheet just now: the button goes");
    }

    /// <summary>A reported payment carries reported_at and no offer: no button.</summary>
    [Fact]
    public void A_reported_payment_offers_nothing()
    {
        var line = Line(", " + Call + """, "reported_at": "2026-11-15T10:00:00Z" """);
        Assert.Null(line.Report);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero), line.ReportedAt);
        Assert.False(ReportPayment.Offers(ReportPayment.Evidence(line, "Refunds", 4)!));

        // Both sent: reported wins, as the iPad's offersReport.
        var both = Line(", " + Call + """, "report": {"trace_id": "t-1", "at": "2026-11-15T09:30:00Z"}, "reported_at": "2026-11-15T10:00:00Z" """);
        Assert.False(ReportPayment.Offers(ReportPayment.Evidence(both, "Refunds", 4)!));
    }

    /// <summary>An offer tafs cloud didn't send whole is none; an entry with no call opens no receipt.</summary>
    [Theory]
    [InlineData("""{"at": "2026-11-15T09:30:00Z"}""")]
    [InlineData("""{"trace_id": "", "at": "2026-11-15T09:30:00Z"}""")]
    [InlineData("""{"trace_id": "t-1"}""")]
    [InlineData("""{"trace_id": "t-1", "at": "yesterday"}""")]
    [InlineData("\"t-1\"")]
    public void A_broken_offer_is_none(string report)
    {
        Assert.Null(Line(", " + Call + ", \"report\": " + report).Report);
    }

    [Fact]
    public void An_entry_without_its_call_opens_nothing()
    {
        var line = Line(""", "report": {"trace_id": "t-1", "at": "2026-11-15T09:30:00Z"}""");
        Assert.Null(line.Call);
        Assert.Null(ReportPayment.Evidence(line, "Refunds", 4));
        // A call KIFF's reader wouldn't take (no outcome) is none too.
        Assert.Null(Line(""", "call": {"proposal_id": "p1", "action_name": "x", "created_at": "2026-11-15T09:30:00Z", "held": false}""").Call);
    }

    /// <summary>The sheet's words: the taf, the entry's amount_text and when the payment was made, a long date with no time.</summary>
    [Fact]
    public void The_sheet_says_what_it_reports_and_that_the_taf_halts()
    {
        var entry = ReportPayment.Evidence(Line(", " + Call + """, "report": {"trace_id": "t-1", "at": "2026-11-15T23:30:00Z"}"""), "Refunds", 4)!;
        var paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        var intro = ReportPayment.Intro(En, entry, paris);
        Assert.StartsWith("Report a payment you didn't expect: Refunds paid €150.00 on ", intro);
        Assert.EndsWith(". We halt Refunds until you've looked.", intro);
        // 23:30 UTC is the 16th in Paris; the date is report.at, and no time is written.
        Assert.Contains("16", intro);
        Assert.Contains("2026", intro);
        Assert.DoesNotContain(":30", intro);
        Assert.Equal("Report and halt Refunds", En.Get("record.report.submit", ("taf", entry.Taf)));
    }

    /// <summary>The route: the receipt's id escaped into the path, with no body.</summary>
    [Fact]
    public void The_report_goes_to_its_receipts_route()
    {
        Assert.Equal("/v1/record/t-1/report", ReportPayment.Path("t-1"));
        Assert.Equal("/v1/record/t%2F1%3Fx%3D1/report", ReportPayment.Path("t/1?x=1"));
        Assert.Equal("{}", System.Text.Encoding.UTF8.GetString(ReportPayment.Body));
    }

    /// <summary>tafs cloud took it when it answers reported_at; a refusal, or an answer without it, didn't go through.</summary>
    [Theory]
    [InlineData("""{"reported_at": "2026-11-15T10:00:00Z", "taf": "f1", "halt": {"state": "halting"}}""", true)]
    [InlineData("""{"reported_at": "2026-11-15T10:00:00.123Z", "halt": {"state": "halted"}}""", true)]
    [InlineData("""{"halt": {"state": "halted"}}""", false)]
    [InlineData("""{"reported_at": "soon"}""", false)]
    [InlineData("""{"error": "not_a_payment", "words": "This call isn't a payment."}""", false)]
    [InlineData("[]", false)]
    public void Took_reads_tafs_clouds_answer(string answer, bool took)
    {
        Assert.Equal(took, ReportPayment.Took(JsonDocument.Parse(answer).RootElement));
    }

    [Fact]
    public void No_answer_did_not_go_through() => Assert.False(ReportPayment.Took(null));
}
