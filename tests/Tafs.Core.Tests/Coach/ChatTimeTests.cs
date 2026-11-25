using System.Globalization;
using Tafs.Core.Coach;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Coach;

/// <summary>When each chat message was sent, as the phones' ChatTime (kiff/tafs docs/chat.md, "When each message was sent").</summary>
[Part("coach")]
public class ChatTimeTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    static readonly DateTimeOffset Now = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);
    static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");

    [Fact]
    public void The_time_under_a_message_follows_this_PC_s_12_or_24_hour_setting_in_the_account_s_zone()
    {
        var at = new DateTimeOffset(2026, 11, 18, 13, 5, 0, TimeSpan.Zero);
        Assert.Equal("14:05", ChatTime.Time(at, Paris, En, twentyFour: true));
        Assert.Equal("2:05 PM", ChatTime.Time(at, Paris, En, twentyFour: false));
        Assert.Equal("13:05", ChatTime.Time(at, TimeZoneInfo.Utc, En, twentyFour: true));
        Assert.True(ChatTime.Uses24Hour(CultureInfo.GetCultureInfo("fr-FR")));
        Assert.False(ChatTime.Uses24Hour(CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void A_day_is_Today_Yesterday_then_its_date_with_the_year_only_when_it_isn_t_this_one()
    {
        Assert.Equal("Today", ChatTime.Day(Now.AddHours(-2), Now, Paris, En));
        Assert.Equal("Yesterday", ChatTime.Day(Now.AddDays(-1), Now, Paris, En));
        Assert.Equal("Mon, Nov 16", ChatTime.Day(Now.AddDays(-2), Now, Paris, En));
        Assert.Equal("Tue, Nov 18, 2025", ChatTime.Day(Now.AddYears(-1), Now, Paris, En));
        // Midnight is the account's: 23:30 UTC on the 17th is already the 18th in Paris.
        Assert.Equal("Today", ChatTime.Day(new DateTimeOffset(2026, 11, 17, 23, 30, 0, TimeSpan.Zero), Now, Paris, En));
    }

    [Theory]
    [InlineData("en-US", "ddd, MMM d")]
    [InlineData("fr-FR", "ddd d MMM")]
    [InlineData("de-DE", "ddd, d. MMM")]
    [InlineData("es-ES", "ddd, d 'de' MMM")]
    [InlineData("ja-JP", "M月d日ddd")]
    public void The_date_keeps_the_language_s_own_order_without_this_year(string culture, string expected) =>
        Assert.Equal(expected, ChatTime.DayPattern(CultureInfo.GetCultureInfo(culture), withYear: false));

    [Fact]
    public void Every_shipped_language_s_date_without_its_year_names_no_year()
    {
        foreach (var dir in Directory.GetDirectories(SharedFiles.Strings))
        {
            var culture = Locale.Culture(Path.GetFileName(dir));
            var pattern = ChatTime.DayPattern(culture, withYear: false);
            // Outside quoted words: no year, and the day and the month still there.
            var bare = System.Text.RegularExpressions.Regex.Replace(pattern, "'[^']*'", "");
            Assert.True(!bare.Contains('y') && bare.Contains('d') && bare.Contains('M'), $"{culture.Name}: {pattern}");
        }
    }

    [Fact]
    public void A_header_where_a_day_starts_in_the_account_s_zone()
    {
        static ThreadItem At(int day, int hour) => new(new ChatMessage(new DateTimeOffset(2026, 11, day, hour, 0, 0, TimeSpan.Zero),
            ChatFrom.Owner, false, "hi", "s"), null);
        IReadOnlyList<ThreadItem> items = [At(17, 10), At(17, 22), At(17, 23), At(18, 9)];
        Assert.True(ChatTime.StartsDay(items, 0, TimeZoneInfo.Utc));
        Assert.False(ChatTime.StartsDay(items, 1, TimeZoneInfo.Utc));
        Assert.False(ChatTime.StartsDay(items, 2, TimeZoneInfo.Utc));
        Assert.True(ChatTime.StartsDay(items, 3, TimeZoneInfo.Utc));
        // In Paris, 23:00 UTC is already the next day.
        Assert.True(ChatTime.StartsDay(items, 2, Paris));
        Assert.False(ChatTime.StartsDay(items, 3, Paris));
    }
}
