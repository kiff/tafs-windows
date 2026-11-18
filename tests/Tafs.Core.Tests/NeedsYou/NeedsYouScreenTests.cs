using Tafs.Core.NeedsYou;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.NeedsYou;

[Part("approvals")]
public class NeedsYouScreenTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 15, 1, 0, 0, TimeSpan.Zero);
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    public static IEnumerable<object[]> Shipped() => WordBook.Shipped(SharedFiles.Strings).Select(l => new object[] { l });

    static HeldCall Call(TimeSpan left, string? about = null, string? reason = null) =>
        new("Refunds", 0, "Refund order #4502", "€480", "", "Over €150 each", Now + left, [], about, reason);

    [Theory]
    [InlineData(577, "9:37 left")]
    [InlineData(59, "0:59 left")]
    [InlineData(3723, "1:02:03 left")]
    [InlineData(0, "expired")]
    [InlineData(-30, "expired")]
    public void The_countdown_reads_minutes_and_seconds_then_expired(int seconds, string expected) =>
        Assert.Equal(expected, NeedsYouScreen.Left(En, Call(TimeSpan.FromSeconds(seconds)), Now));

    /// <summary>The countdown's "expired" and the answers giving way turn at the same moment.</summary>
    [Theory]
    [InlineData(1000, false)]
    [InlineData(999, true)]
    [InlineData(500, true)]
    [InlineData(0, true)]
    public void The_countdown_and_the_answers_turn_together(int milliseconds, bool expired)
    {
        var call = Call(TimeSpan.FromMilliseconds(milliseconds));
        var lines = NeedsYouScreen.Card(En, call, Now);
        Assert.Equal(expired, NeedsYouScreen.IsExpired(call, Now));
        Assert.Equal(expired, lines.Left == "expired");
        Assert.Equal(!expired, lines.Answerable);
    }

    [Fact]
    public void While_it_can_be_answered_nothing_is_sent_until_you_approve()
    {
        var lines = NeedsYouScreen.Card(En, Call(TimeSpan.FromMinutes(5)), Now);
        Assert.True(lines.Answerable);
        Assert.Equal("Nothing is sent until you approve.", lines.Footer);
        Assert.Equal(("Refunds", "R", "€480", "Over €150 each"), (lines.Holder, lines.Initial, lines.Amount, lines.Reason));
        Assert.Null(lines.Context);
        Assert.Null(lines.TafSays);
    }

    [Fact]
    public void Once_it_lapses_KIFF_refused_it_and_there_is_nothing_to_answer()
    {
        var lines = NeedsYouScreen.Card(En, Call(TimeSpan.FromSeconds(-1)), Now);
        Assert.False(lines.Answerable);
        Assert.Equal("Expired. Nobody answered in time, so KIFF refused it and nothing was sent.", lines.Footer);
        Assert.Equal("expired", lines.Left);
    }

    [Fact]
    public void The_taf_s_own_words_sit_under_their_own_heading_and_never_replace_KIFF_s()
    {
        var lines = NeedsYouScreen.Card(En, Call(TimeSpan.FromMinutes(5), about: "Order 1042", reason: "Arrived broken"), Now);
        Assert.Equal("The taf says", lines.TafSays);
        Assert.Equal(("Order 1042", "Arrived broken"), (lines.TafAbout, lines.TafReason));
        Assert.Equal(("Refund order #4502", "€480", "Over €150 each"), (lines.Summary, lines.Amount, lines.Reason));
    }

    [Fact]
    public void In_the_sidebar_and_on_the_Overview_a_held_call_is_one_line_and_its_time()
    {
        var call = NeedsYouScreen.Sample(En, Now)[0] with { ExpiresAt = new DateTimeOffset(2026, 11, 15, 0, 27, 0, TimeSpan.Zero) };
        Assert.Equal(("Deploys: Run migration 0042_orders_index", "Schema migrations: always asks you · answer by 00:27"),
            NeedsYouScreen.SidebarRow(En, call, TimeZoneInfo.Utc));
        Assert.Equal(("Deploys · Run migration 0042_orders_index", "answer by 00:27"), NeedsYouScreen.OverviewRow(En, call, TimeZoneInfo.Utc));
        var paris = TimeZoneInfo.CreateCustomTimeZone("Paris", TimeSpan.FromHours(1), "Paris", "Paris");
        Assert.Equal("answer by 01:27", NeedsYouScreen.AnswerBy(En, call, paris));
    }

    [Fact]
    public void Until_this_PC_approves_the_answers_are_words_and_the_way_to_add_it()
    {
        var gate = NeedsYouScreen.Gate(En);
        Assert.Contains("can't approve", gate.Lead);
        Assert.Equal("Add this Windows PC", gate.Add);
    }

    [Fact]
    public void The_samples_are_the_iPad_s_three_calls()
    {
        var calls = NeedsYouScreen.Sample(En, Now);
        Assert.Equal(new[] { "Deploys", "Late parcels", "Support replies" }, calls.Select(c => c.Taf));
        Assert.Equal(new[] { 1, 3, 4 }, calls.Select(c => c.TafColor));
        Assert.Equal("Run migration 0042_orders_index", calls[0].Summary);
        Assert.Equal("Schema migrations: always asks you", calls[0].Reason);
        Assert.Equal(("Send a new parcel for order #5098", "Reships: always asks you"), (calls[1].Summary, calls[1].Reason));
        Assert.Equal(("Promise a replacement on ticket #7802", "Promises: always asks you"), (calls[2].Summary, calls[2].Reason));
        Assert.Equal(new[] { "Wait one more day" }, calls[1].NextSteps);
        Assert.Equal(new[] { "Ask for a photo first" }, calls[2].NextSteps);
        // No sample call moves money.
        Assert.All(calls, c => Assert.Null(c.Amount));
        Assert.All(calls, c => Assert.Equal("9:37 left", NeedsYouScreen.Left(En, c, Now)));
    }

    /// <summary>The spaces are the system's own (Windows and ICU pick different no-break spaces), so they're compared as spaces.</summary>
    [Theory]
    [InlineData("en", "€8,400")]
    [InlineData("fr", "8 400 €")]
    public void Euros_are_written_the_language_s_way(string language, string expected)
    {
        var euros = NeedsYouScreen.Euros(WordBook.Load(SharedFiles.Strings, language), 8_400);
        Assert.Equal(expected.Replace(" ", ""), string.Concat(euros.Where(c => !char.IsWhiteSpace(c))));
        Assert.True(expected.Count(c => c == ' ') == euros.Count(char.IsWhiteSpace), $"{language}: {euros}");
    }

    [Fact]
    public void Portuguese_money_is_written_as_in_Portugal() =>
        Assert.EndsWith("€", NeedsYouScreen.Euros(WordBook.Load(SharedFiles.Strings, "pt"), 8_400));

    [Fact]
    public void A_taf_s_initial_and_colour()
    {
        Assert.Equal("É", NeedsYouScreen.Initial("équipe"));
        Assert.Equal(0xE51255u, TafColors.Of(1));
        Assert.Equal(TafColors.Of(0), TafColors.Of(6));
        Assert.Equal(TafColors.Of(5), TafColors.Of(-1));
    }

    [Theory]
    [MemberData(nameof(Shipped))]
    public void Every_line_is_there_in_every_shipped_language(string language)
    {
        var w = WordBook.Load(SharedFiles.Strings, language);
        var calls = NeedsYouScreen.Sample(w, Now);
        var cards = calls.Select(c => NeedsYouScreen.Card(w, c, Now))
            .Concat([NeedsYouScreen.Card(w, calls[0] with { ExpiresAt = Now }, Now)]);
        var empty = NeedsYouScreen.Empty(w);
        var all = cards.SelectMany(l => new[] { l.Holder, l.Left, l.Summary, l.Reason, l.Footer, l.Amount ?? "x", l.Context ?? "x" })
            .Concat(calls.SelectMany(c => c.NextSteps))
            .Concat([NeedsYouScreen.Title(w), empty.Lead, empty.Hint, NeedsYouScreen.Gate(w).Lead])
            .Concat(calls.SelectMany(c => new[] { NeedsYouScreen.SidebarRow(w, c, TimeZoneInfo.Utc).Line, NeedsYouScreen.OverviewRow(w, c, TimeZoneInfo.Utc).AnswerBy }));
        Assert.All(all, line => { Assert.False(string.IsNullOrWhiteSpace(line)); Assert.DoesNotContain("{", line); });
    }
}
