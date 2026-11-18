using System.Globalization;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>A taf's page's tabs, in the iPad's order: Chat first, since talking to a taf is the main thing.</summary>
public enum TafTab { Chat, Activity, Card, Setup }

/// <summary>A taf's page's head: its avatar's letter and colour, its name, and its state ("Working").</summary>
public sealed record TafHead(string Name, string Initial, int Colour, string State, bool Waiting = false);

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
        new(name, Initial(name), colour, w.Get("taf.state." + stateKey), stateKey == "waiting");

    /// <summary>The sample's taf the reference set opens (taf.png): Support replies, the fifth colour, working.</summary>
    public static TafHead SampleHead(WordBook w) => Head(w, SampleTaf(w), 4, "working");

    public static string SampleTaf(WordBook w) => w.Get("sample.support.name");

    /// <summary>
    /// The sample's own Card, as the iPad's sample keeps it for Support replies: up to 40 replies a day (as kiff.dev/tafs's
    /// example Card says), a promise of a refund, a discount or a replacement always asks, and a customer's details are never
    /// sent on; 1 reply used today. No money moves.
    /// </summary>
    public static TafCard SampleOwnCard(WordBook w)
    {
        var day = CardWindow.Of(w, "calendar_day");
        return new TafCard("sample-own", SampleTaf(w),
        [
            new("reply_ticket", w.Get("sample.support.rule.replies"), day, CountPerWindow: 40),
            new("promise", w.Get("sample.support.rule.promises"), day, AlwaysAsk: true),
            new("share_customer_data", w.Get("sample.support.rule.customer_data"), day, Never: true, Outside: Outside.Refuse),
        ], Calls: new CallsTotal(40, 1, day));
    }

    /// <summary>
    /// The Card the reference set's taf-card.png shows for Support replies, as KIFF would list it: its live Card, named for
    /// its helpdesk, with its replies counted against the day's limit (12 of 40).
    /// </summary>
    public static IReadOnlyList<TafCard> SampleCards(WordBook w)
    {
        var day = CardWindow.Of(w, "calendar_day");
        return [new TafCard("sample-support", w.Get("sample.card.refunds_stripe"), SampleOwnCard(w).Rules, Calls: new CallsTotal(40, 12, day))];
    }

    /// <summary>The sample's Card bar above its Activity, as taf.png: "39 calls left today of 40", from its one Card.</summary>
    public static CardBarLine SampleCard(WordBook w) => SampleOwnCard(w).Bar(w, 1);
}
