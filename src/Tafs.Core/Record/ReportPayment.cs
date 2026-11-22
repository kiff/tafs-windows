using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Record;

/// <summary>
/// tafs cloud's offer to report a payment (an entry's <c>report: {trace_id, at}</c>): its receipt's id and when the payment
/// was made. tafs cloud sends it only for a payment this reader may report that isn't reported yet.
/// </summary>
public sealed record ReportOffer(string TraceId, DateTimeOffset At);

/// <summary>
/// "Report this payment" (tafs docs/report-payment.md, the iPad's <c>ReportPaymentSheet</c>): a payment the person didn't
/// expect, reported from its receipt. KIFF marks the receipt and the taf is halted, as the Halt button does. It takes no
/// note and no reason, and no Windows Hello, since it only takes authority away; resuming the taf stays behind the phone.
/// </summary>
public static class ReportPayment
{
    /// <summary><c>POST /v1/record/{trace_id}/report</c>, with no body, for an owner or admin.</summary>
    public static string Path(string traceId) => "/v1/record/" + Uri.EscapeDataString(traceId) + "/report";

    /// <summary>What's sent: no body, written as the iPad's empty object.</summary>
    public static byte[] Body => "{}"u8.ToArray();

    /// <summary>An entry's offer, as tafs cloud sends it: none without a receipt id or a time it can read.</summary>
    public static ReportOffer? Offer(JsonElement entry) =>
        Lenient.Field(entry, "report") is { ValueKind: JsonValueKind.Object } r && Lenient.Text(r, "trace_id") is { Length: > 0 } trace
            && Lenient.Text(r, "at") is { } at && KiffActivity.Parse(at) is { } when
            ? new ReportOffer(trace, when) : null;

    /// <summary>When an entry was reported (<c>reported_at</c>), which takes the button away; none when it isn't.</summary>
    public static DateTimeOffset? ReportedAt(JsonElement entry) =>
        Lenient.Text(entry, "reported_at") is { } at ? KiffActivity.Parse(at) : null;

    /// <summary>
    /// The receipt offers the report, as the iPad's <c>offersReport</c>: where tafs cloud sent an offer, the payment isn't
    /// reported yet, and it wasn't reported from this sheet just now.
    /// </summary>
    public static bool Offers(RecordEntry e, bool reportedNow = false) => e.Report is not null && e.ReportedAt is null && !reportedNow;

    /// <summary>
    /// "Report a payment you didn't expect: {taf} paid {amount} on {date}. We halt {taf} until you've looked." The amount is the
    /// entry's <c>amount_text</c>; the date is when the payment was made, a long date in the app's language with no time.
    /// </summary>
    public static string Intro(WordBook w, RecordEntry e, TimeZoneInfo zone) =>
        w.Get("record.report.intro", ("taf", e.Taf), ("amount", e.Amount ?? ""), ("date", Date(w, e.Report?.At ?? e.At, zone)));

    /// <summary>A long date with no time, in the app's language, in <paramref name="zone"/>.</summary>
    public static string Date(WordBook w, DateTimeOffset at, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(at, zone).ToString("D", Locale.Culture(w.Language));

    /// <summary>
    /// tafs cloud took it: its answer is <c>{reported_at, taf?, halt}</c>. Reporting again answers the same, so sending again is
    /// safe. Anything else (a refusal, or an answer without <c>reported_at</c>) is "Couldn't send the report".
    /// </summary>
    public static bool Took(JsonElement? answer) =>
        answer is { ValueKind: JsonValueKind.Object } a && Lenient.Text(a, "reported_at") is { } at && KiffActivity.Parse(at) is not null;

    /// <summary>
    /// A call from tafs cloud's Activity, the Record's search or a case, as its receipt opens it (the iPad's
    /// <c>SourceActivityRow.evidence</c>): tafs cloud's kind, title, detail and amount kept as sent, KIFF's call for the receipt,
    /// and the report offer. None for an entry with no call.
    /// </summary>
    public static RecordEntry? Evidence(ActivityLine line, string taf, int colour) =>
        line.Call is { } call
            ? new RecordEntry(taf, colour, line.Kind, line.Title, line.Detail, call.CreatedAt, line.Amount, call, line.Report, line.ReportedAt)
            : null;
}
