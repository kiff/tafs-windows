using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>A sample taf: its key in the sample's words, its name and colour, and its own Card.</summary>
public sealed record SampleTaf(string Key, string Name, int Colour, TafCard Card);

/// <summary>
/// The iPad's five sample tafs, in the reference set's overview.png order, each with its Card's total as the sample's Record
/// has used it: Deploys 1 of 10 deploys today, Bill pay €1,240 of €25,000 this week, Refunds €40 of €2,000 today,
/// Chargebacks €35 of €500 this week, Access 1 of 40 roles today.
/// </summary>
public static class SampleTafs
{
    public static IReadOnlyList<SampleTaf> All(WordBook w)
    {
        var day = CardWindow.Of(w, "calendar_day");
        var week = CardWindow.Of(w, "week");
        SampleTaf Taf(string key, int colour, CardTotal? total = null, CallsTotal? calls = null)
        {
            var name = w.Get($"sample.{key}.name");
            return new(key, name, colour, new TafCard($"sample-{key}", name, [], total, calls));
        }
        return
        [
            Taf("deploys", 1, calls: new CallsTotal(10, 1, day)),
            Taf("bill_pay", 3, new CardTotal(KiffAmount.Euros(25_000), KiffAmount.Euros(1_240), week)),
            new("refunds", TafScreen.SampleTaf(w), 4, TafScreen.SampleOwnCard(w)),
            Taf("chargebacks", 0, new CardTotal(KiffAmount.Euros(500), KiffAmount.Euros(35), week)),
            Taf("access", 2, calls: new CallsTotal(40, 1, day)),
        ];
    }
}
