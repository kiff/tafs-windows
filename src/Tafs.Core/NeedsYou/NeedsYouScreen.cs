using System.Globalization;
using Tafs.Core.Pairing;
using Tafs.Core.Words;

namespace Tafs.Core.NeedsYou;

/// <summary>
/// A call KIFF held because it's outside its Card: what the taf asks, why it was held, and until when
/// it can be answered. <paramref name="Amount"/> is already in the person's money and language.
/// </summary>
public sealed record HeldCall(
    string Taf, int TafColor, string Summary, string? Amount, string Context, string Reason,
    DateTimeOffset ExpiresAt, IReadOnlyList<string> NextSteps, string? TafAbout = null, string? TafReason = null)
{
    /// <summary>What it's on, one line an argument as KIFF keeps them, under Details (the iPad's <c>details</c>).</summary>
    public IReadOnlyList<string> Details { get; init; } = [];

    /// <summary>KIFF's id for the hold, when it came from KIFF.</summary>
    public string? KiffId { get; init; }

    /// <summary>KIFF's accounting version for the hold, signed with the answer.</summary>
    public int? AccountingVersion { get; init; }

    /// <summary>
    /// Whether approving it once can pass it (the iPad's <c>approvableOnce</c>, KIFF's <c>can_approve_once ?? true</c>): not when
    /// it's over a total the Card keeps across calls.
    /// </summary>
    public bool ApprovableOnce { get; init; } = true;

    /// <summary>
    /// Coach's own hold of a version to install (the iPad's <c>install</c>): approving it once isn't how it goes live, so it's
    /// installed from its card. None for any other held call.
    /// </summary>
    public VersionInstall? Install { get; init; }

    /// <summary>Whether it's a listed taf's: only those are in the sidebar's NEEDS YOU. Coach's and an unknown agent's aren't.</summary>
    public bool Listed { get; init; } = true;

    /// <summary>Whether the card draws an avatar: a taf's and Coach's; an unknown agent's has none.</summary>
    public bool Avatar { get; init; } = true;
}

/// <summary>
/// What a held call's card says where its answers go, as the iPad's <c>HeldCallCard.answers</c> for a PC that doesn't approve
/// yet: a line, and whether the gate (why, and Add this Windows PC) stands where the iPad's <c>ApprovalGate</c> would.
/// </summary>
public sealed record HeldAnswerLines(string? Line, bool Gate);

/// <summary>The version a hold of Coach's would install, as the iPad's <c>VersionInstall</c>: the taf and its version's number.</summary>
public sealed record VersionInstall(string Taf, int N);

/// <summary>What a held call's card says, top to bottom, as the iPad's HeldCallCard.</summary>
public sealed record HeldCallLines(
    string Holder, string Initial, string Left, string? Amount, string Summary, string? Context, string Reason,
    string? TafSays, string? TafAbout, string? TafReason, string Footer, bool Answerable);

/// <summary>Needs you (the iPad's NeedsYouView): the held calls, each on its card, or that nothing waits.</summary>
public static class NeedsYouScreen
{
    public static string Title(WordBook w) => w.Get("needs_you.title");

    /// <summary>With nothing waiting: the panel's two lines.</summary>
    public static (string Lead, string Hint) Empty(WordBook w) => (w.Get("needs_you.nothing_waiting"), w.Get("needs_you.empty_hint"));

    /// <summary>
    /// Under a second left counts as lapsed, so the countdown's "expired" and the answers giving way turn
    /// together. KIFF decides either way: this is only what the card shows.
    /// </summary>
    public static bool IsExpired(HeldCall call, DateTimeOffset now) => call.ExpiresAt - now < TimeSpan.FromSeconds(1);

    /// <summary>"9:37 left", counted down every second; "expired" once it lapses. An hour or more reads 1:02:03.</summary>
    public static string Left(WordBook w, HeldCall call, DateTimeOffset now)
    {
        if (IsExpired(call, now)) return w.Get("needs_you.expired");
        var left = call.ExpiresAt - now;
        var seconds = (long)left.TotalSeconds;
        var time = seconds >= 3600
            ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
            : $"{seconds / 60}:{seconds % 60:00}";
        return w.Get("needs_you.left", ("time", time));
    }

    /// <summary>
    /// The card's lines. While it can be answered, "Nothing is sent until you approve." sits over the answers;
    /// once it has lapsed, the line says KIFF refused it, and there's nothing left to answer.
    /// The taf's own words, when it sent some, go under their own heading and never replace KIFF's.
    /// </summary>
    public static HeldCallLines Card(WordBook w, HeldCall call, DateTimeOffset now)
    {
        var expired = IsExpired(call, now);
        var tafSpoke = call.TafAbout is not null || call.TafReason is not null;
        return new(
            call.Taf, Initial(call.Taf), Left(w, call, now), call.Amount, call.Summary,
            string.IsNullOrEmpty(call.Context) ? null : call.Context, call.Reason,
            tafSpoke ? w.Get("needs_you.taf_says") : null, call.TafAbout, call.TafReason,
            w.Get(expired ? "needs_you.expired_line" : "needs_you.nothing_sent"), !expired);
    }

    /// <summary>"answer by 00:27": when the call lapses, in this PC's time, 24-hour, as the iPad shows it.</summary>
    public static string AnswerBy(WordBook w, HeldCall call, TimeZoneInfo zone) =>
        w.Get("needs_you.answer_by", ("time", TimeZoneInfo.ConvertTime(call.ExpiresAt, zone).ToString("HH:mm", CultureInfo.InvariantCulture)));

    /// <summary>
    /// A held call in the sidebar, under NEEDS YOU (the iPad's heldRow): "Deploys: Run migration…", then
    /// "Schema migrations: always asks you · answer by 00:27".
    /// </summary>
    public static (string Title, string Line) SidebarRow(WordBook w, HeldCall call, TimeZoneInfo zone)
    {
        var time = TimeZoneInfo.ConvertTime(call.ExpiresAt, zone).ToString("HH:mm", CultureInfo.InvariantCulture);
        return ($"{call.Taf}: {call.Summary}", w.Get("home.reason_answer_by", ("why", call.Reason), ("time", time)));
    }

    /// <summary>A held call on the Overview, under NEEDS YOU: "Deploys · Run migration…", and "answer by 00:27" at its end.</summary>
    public static (string Title, string AnswerBy) OverviewRow(WordBook w, HeldCall call, TimeZoneInfo zone) =>
        ($"{call.Taf} · {call.Summary}", AnswerBy(w, call, zone));

    /// <summary>
    /// Where the answers go, until this PC approves with Windows Hello (its own change): why it can't, and Add
    /// this Windows PC. Words, as the iPad's ApprovalNotReady, never buttons that can't work.
    /// </summary>
    public static ViewOnlyLines Gate(WordBook w) => PairingScreen.ViewOnly(w);

    /// <summary>
    /// Where the answers go, as the iPad's <c>answers(refuseOnly:)</c>: someone without <c>holds.answer</c> reads who answers,
    /// and no gate; a call approving once can pass has the gate where Approve would be; one over a Card total says so
    /// ("Extra room lets only this call through." for someone with <c>cards.manage</c>), the gate only where a manager's Extra
    /// room would be. The gate stands only while this PC can't approve, as the iPad's <c>ApprovalGate</c>: a view-only session,
    /// or none (<paramref name="mode"/> null); a PC that approves reads the line with no gate.
    /// </summary>
    public static HeldAnswerLines Answers(WordBook w, HeldCall call, bool mayAnswerHolds, bool managesCards, Kiff.ApprovalMode? mode)
    {
        var gate = mode is null or Kiff.ApprovalMode.View;
        return !mayAnswerHolds ? new(w.Get("needs_you.someone_answers"), false)
            : call.ApprovableOnce ? new(null, gate)
            : new(w.Get(managesCards ? "needs_you.over_total_manager" : "needs_you.over_total"), managesCards && gate);
    }

    /// <summary>The letter on a taf's avatar: its name's first, in capitals.</summary>
    public static string Initial(string name) =>
        name.Length == 0 ? "?" : StringInfo.GetNextTextElement(name).ToUpper(CultureInfo.InvariantCulture);

    /// <summary>
    /// Euros in the language's own way: "€8,400" in English, "8 400 €" in French. tafs' Portuguese is
    /// Portugal's (telemóvel, registo), so its money is written as in Portugal, not Brazil.
    /// </summary>
    public static string Euros(WordBook w, decimal amount)
    {
        var culture = w.Language == "pt" ? "pt-PT" : w.Language;
        var format = (NumberFormatInfo)CultureInfo.GetCultureInfo(culture).NumberFormat.Clone();
        format.CurrencySymbol = "€";
        return amount.ToString(decimal.Truncate(amount) == amount ? "C0" : "C2", format);
    }

    /// <summary>
    /// The iPad's two sample calls (TafTemplates), in the person's language: Deploys' migration, which its
    /// Card always asks about, and Bill pay's €8,400, over €5,000 each. For screenshots, and the docs.
    /// </summary>
    public static IReadOnlyList<HeldCall> Sample(WordBook w, DateTimeOffset now)
    {
        var lapses = now + new TimeSpan(0, 9, 37);
        return
        [
            new(w.Get("sample.deploys.name"), 1, w.Get("sample.deploys.migration"), null, w.Get("sample.deploys.migration_why"),
                w.Get("card.check.always_asks", ("rule", w.Get("sample.deploys.rule.migrations"))), lapses,
                [w.Get("sample.deploys.next_step")]),
            new(w.Get("sample.bill_pay.name"), 3, w.Get("sample.bill_pay.pay_atlas"), Euros(w, 8_400), w.Get("sample.bill_pay.pay_atlas_why"),
                w.Get("card.check.over_each", ("amount", Euros(w, 5_000))), lapses,
                [w.Get("sample.bill_pay.next_step")]),
        ];
    }
}

/// <summary>A taf's colour (the iPad's Theme.tafColors): bright, with white text at 4.5:1 or more.</summary>
public static class TafColors
{
    static readonly uint[] Colors = [0xC25100, 0xE51255, 0x7B4DFF, 0x2A64F5, 0x0B7DA4, 0x128462];

    /// <summary>The colour for a taf's index, wrapping around, as RGB.</summary>
    public static uint Of(int index) => Colors[((index % Colors.Length) + Colors.Length) % Colors.Length];
}
