using System.Globalization;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>A taf's page's tabs, in the iPad's order: Chat first, since talking to a taf is the main thing.</summary>
public enum TafTab { Chat, Activity, Card, Setup }

/// <summary>A taf's page's head: its avatar's letter and colour, its name, and its state ("Working").</summary>
public sealed record TafHead(string Name, string Initial, int Colour, string State);

/// <summary>The Card bar above the Activity: what's left of its main limit ("€1,960 left today of €2,000") and the Card's name.</summary>
public sealed record CardBarLine(string Hero, string Under);

/// <summary>A taf's page (the iPad's TafDetailView): the head, the tabs, and above Chat and Activity, the Card bar.</summary>
public static class TafScreen
{
    public static IReadOnlyList<(TafTab Tab, string Title)> Tabs(WordBook w) =>
    [
        (TafTab.Chat, w.Get("taf.section.chat")),
        (TafTab.Activity, w.Get("taf.section.activity")),
        (TafTab.Card, w.Get("taf.section.card")),
        (TafTab.Setup, w.Get("taf.section.setup")),
    ];

    /// <summary>The tabs' accessible name: "View".</summary>
    public static string TabsName(WordBook w) => w.Get("taf.view_picker");

    /// <summary>The avatar's letter: the name's first character as one reads it, in capitals.</summary>
    public static string Initial(string name) =>
        string.IsNullOrWhiteSpace(name) ? "" : StringInfo.GetNextTextElement(name.Trim()).ToUpper(CultureInfo.CurrentCulture);

    public static TafHead Head(WordBook w, string name, int colour, string stateKey) =>
        new(name, Initial(name), colour, w.Get("taf.state." + stateKey));

    /// <summary>The sample's taf the reference set opens (taf.png): Refunds, the fifth colour, working.</summary>
    public static TafHead SampleHead(WordBook w) => Head(w, SampleTaf(w), 4, "working");

    public static string SampleTaf(WordBook w) => w.Get("sample.refunds.name");

    /// <summary>
    /// The sample's own Card, as the iPad's sample keeps it for Refunds: refunds up to €150 each and €2,000 a day, credits
    /// up to €100 each and €1,000 a day (past them it asks), never a payout; €40 used today.
    /// </summary>
    public static TafCard SampleOwnCard(WordBook w)
    {
        var day = CardWindow.Of(w, "calendar_day");
        return new TafCard("sample-own", SampleTaf(w),
        [
            new("refund", w.Get("sample.refunds.rule.refunds"), day, KiffAmount.Euros(150), KiffAmount.Euros(2_000)),
            new("credit", w.Get("sample.refunds.rule.credits"), day, KiffAmount.Euros(100), KiffAmount.Euros(1_000)),
            new("payout", w.Get("sample.refunds.rule.payouts"), day, Never: true, Outside: Outside.Refuse),
        ], new CardTotal(KiffAmount.Euros(2_000), KiffAmount.Euros(40), day));
    }

    /// <summary>The sample's Card bar above its Activity, as taf.png: "€1,960 left today of €2,000", from its one Card.</summary>
    public static CardBarLine SampleCard(WordBook w) => SampleOwnCard(w).Bar(w, 1);
}
