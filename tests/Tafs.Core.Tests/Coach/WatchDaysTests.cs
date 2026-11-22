using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Coach;

/// <summary>W11 "From your watch", as the iPad's WatchDaysTests: the days, a day's turns, titles and times.</summary>
[Part("coach")]
public class WatchDaysTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    static JsonElement J(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void The_days_read_newest_first_with_today_and_a_broken_day_is_left_out()
    {
        var list = WatchDays.ReadList(J("""{"days":[{"date":"2026-11-22","count":3},{"date":"2026-11-21"},{"date":"2026-11-20","count":1}],"today":"2026-11-22"}"""));
        Assert.Equal(new[] { new WatchDay("2026-11-22", 3), new WatchDay("2026-11-20", 1) }, list.Days);
        Assert.Equal("2026-11-22", list.Today);
        Assert.Empty(WatchDays.ReadList(J("{}")).Days);
    }

    [Fact]
    public async Task A_switch_that_is_off_is_no_days_never_an_error()
    {
        var (days, why) = await WatchDays.LoadAsync(_ => Task.FromResult(new CloudRead(null, "not found", 404, "watch_off")));
        Assert.Null(why);
        Assert.Empty(days!.Days);
        var (none, said) = await WatchDays.LoadAsync(_ => Task.FromResult(new CloudRead(null, "It didn't reach the server.")));
        Assert.Null(none);
        Assert.Equal("It didn't reach the server.", said);
    }

    [Fact]
    public void A_day_s_turns_keep_their_order_and_leave_out_messages_to_a_taf()
    {
        var turns = WatchDays.ReadDay(J("""
            {"turns":[
              {"id":"t1","said":"Refund Ana","said_at":"2026-11-22T08:05:00Z","taf":{"id":"taf_1","name":"Refunds"},"state":"done","line":"Refunded €12","words":"Done"},
              {"id":"t2","said":"And Bo?","said_at":"2026-11-22T08:07:00Z","state":"working","line":"","words":"Working on it","reply_to":"t1"},
              {"id":"t3","said":"Hi Refunds","kind":"taf","state":"done","words":"Done"},
              {"said":"no id"}]}
            """));
        Assert.Equal(new[] { "t1", "t2" }, turns.Select(t => t.Id));
        Assert.Equal(("Refunds", "Refunded €12"), (turns[0].TafName, turns[0].Outcome));
        Assert.Equal(("Working on it", "t1"), (turns[1].Outcome, turns[1].ReplyTo));
    }

    [Fact]
    public void A_day_is_Today_by_tafs_cloud_s_today_else_its_date()
    {
        var paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        var now = DateTimeOffset.Parse("2026-11-22T23:30:00Z");
        Assert.Equal("Today", WatchDays.Title(En, "2026-11-22", "2026-11-22", paris, now));
        // With no today from tafs cloud, the account's zone decides: 23:30 UTC is already the 23rd in Paris.
        Assert.Equal("Today", WatchDays.Title(En, "2026-11-23", null, paris, now));
        Assert.Equal("Saturday 21 November", WatchDays.Title(En, "2026-11-21", "2026-11-22", paris, now));
        Assert.Equal("09:05", WatchDays.Time("2026-11-22T08:05:00Z", paris));
        Assert.Null(WatchDays.Time("", paris));
    }
}
