using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>A sample taf: its key in the sample's words, its name and colour, and its own Card.</summary>
public sealed record SampleTaf(string Key, string Name, int Colour, TafCard Card);

/// <summary>
/// The iPad's five sample tafs, in the reference set's overview.png order, each with its Card's total as the sample's Record
/// has used it: Deploys 1 of 10 deploys today, Late parcels 1 of 100 delay messages, Support replies 12 of 40 replies, Lead
/// follow-up 2 of 50 replies, Access 1 of 40 roles today. No sample moves money: each Card counts, asks and refuses. Each
/// Card holds its template's rules, as the iPad starts each sample taf from its template (<c>TafTemplates</c>).
/// </summary>
public static class SampleTafs
{
    public static IReadOnlyList<SampleTaf> All(WordBook w)
    {
        var day = CardWindow.Of(w, "calendar_day");
        SampleTaf Taf(string key, int colour, IReadOnlyList<CardRule> rules, CallsTotal calls)
        {
            var name = w.Get($"sample.{key}.name");
            return new(key, name, colour, new TafCard($"sample-{key}", name, rules, Calls: calls));
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
            ], new CallsTotal(10, 1, day)),
            Taf("parcels", 3,
            [
                new("delay_message", Rule("parcels", "delay_messages"), day, CountPerWindow: 100),
                new("reship", Rule("parcels", "reships"), day, AlwaysAsk: true),
                new("change_address", Rule("parcels", "address_changes"), day, Never: true, Outside: Outside.Refuse),
                new("parcel_refund", Rule("parcels", "refunds"), day, Never: true, Outside: Outside.Refuse),
            ], new CallsTotal(100, 1, day)),
            new("support", TafScreen.SampleTaf(w), 4, TafScreen.SampleOwnCard(w)),
            Taf("leads", 0,
            [
                new("reply_lead", Rule("leads", "replies"), day, CountPerWindow: 50),
                new("offer_discount", Rule("leads", "discounts"), day, AlwaysAsk: true),
            ], new CallsTotal(50, 2, day)),
            Taf("access", 2,
            [
                new("grant_role", Rule("access", "approved_roles"), day, CountPerWindow: 40),
                new("deactivate", Rule("access", "deactivations"), day, CountPerWindow: 20),
                new("elevate", Rule("access", "elevated"), day, AlwaysAsk: true),
                new("grant_admin", Rule("access", "admin_roles"), day, Never: true, Outside: Outside.Refuse),
                new("delete_user", Rule("access", "deleting_users"), day, Never: true, Outside: Outside.Refuse),
            ], new CallsTotal(40, 1, day)),
        ];
    }
}
