using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>A sample taf: its key in the sample's words, its name and colour, and its own Card.</summary>
public sealed record SampleTaf(string Key, string Name, int Colour, TafCard Card);

/// <summary>
/// The iPad's five sample tafs, in the reference set's overview.png order, each with its Card's total as the sample's Record
/// has used it: Deploys 1 of 10 deploys today, Bill pay €1,240 of €25,000 this week, Refunds €40 of €2,000 today,
/// Chargebacks €35 of €500 this week, Access 1 of 40 roles today. Each Card holds its template's rules, as the iPad starts
/// each sample taf from its template (<c>TafTemplates</c>).
/// </summary>
public static class SampleTafs
{
    public static IReadOnlyList<SampleTaf> All(WordBook w)
    {
        var day = CardWindow.Of(w, "calendar_day");
        var week = CardWindow.Of(w, "week");
        SampleTaf Taf(string key, int colour, IReadOnlyList<CardRule> rules, CardTotal? total = null, CallsTotal? calls = null)
        {
            var name = w.Get($"sample.{key}.name");
            return new(key, name, colour, new TafCard($"sample-{key}", name, rules, total, calls));
        }
        string Rule(string key, string rule) => w.Get($"sample.{key}.rule.{rule}");
        return
        [
            Taf("deploys", 1,
            [
                new("deploy", Rule("deploys", "deploys"), day, CountPerWindow: 10),
                new("rollback", Rule("deploys", "rollbacks"), day, CountPerWindow: 20),
                new("migration", Rule("deploys", "migrations"), day, AlwaysAsk: true),
                new("destroy_data", Rule("deploys", "destroy_data"), day, Never: true, Outside: Outside.Refuse),
            ], calls: new CallsTotal(10, 1, day)),
            Taf("bill_pay", 3,
            [
                new("pay_bill", Rule("bill_pay", "bills"), week, KiffAmount.Euros(5_000), KiffAmount.Euros(25_000)),
                new("wire", Rule("bill_pay", "wires"), day, AlwaysAsk: true),
                new("bank_details", Rule("bill_pay", "bank_details"), day, Never: true, Outside: Outside.Refuse),
                new("new_payee", Rule("bill_pay", "new_payees"), day, Never: true, Outside: Outside.Refuse),
            ], new CardTotal(KiffAmount.Euros(25_000), KiffAmount.Euros(1_240), week)),
            new("refunds", TafScreen.SampleTaf(w), 4, TafScreen.SampleOwnCard(w)),
            Taf("chargebacks", 0,
            [
                new("submit_evidence", Rule("chargebacks", "evidence"), day, KiffAmount.Euros(500), CountPerWindow: 50),
                new("accept_dispute", Rule("chargebacks", "accepting_loss"), week, KiffAmount.Euros(100), KiffAmount.Euros(500)),
            ], new CardTotal(KiffAmount.Euros(500), KiffAmount.Euros(35), week)),
            Taf("access", 2,
            [
                new("grant_role", Rule("access", "approved_roles"), day, CountPerWindow: 40),
                new("deactivate", Rule("access", "deactivations"), day, CountPerWindow: 20),
                new("elevate", Rule("access", "elevated"), day, AlwaysAsk: true),
                new("grant_admin", Rule("access", "admin_roles"), day, Never: true, Outside: Outside.Refuse),
                new("delete_user", Rule("access", "deleting_users"), day, Never: true, Outside: Outside.Refuse),
            ], calls: new CallsTotal(40, 1, day)),
        ];
    }
}
