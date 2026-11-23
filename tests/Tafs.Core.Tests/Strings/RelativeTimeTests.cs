using Tafs.Core.Words;
namespace Tafs.Core.Tests.Strings;

[Part("strings")]
public class RelativeTimeTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("en", 3, "3 hours ago")]
    [InlineData("ja", 3, "3 時間前")]
    [InlineData("ar", 1, "قبل ساعة واحدة")]
    [InlineData("ar", 2, "قبل ساعتين")]
    [InlineData("ar", 3, "قبل 3 ساعات")]
    [InlineData("ar", 11, "قبل 11 ساعة")]
    [InlineData("he", 2, "לפני שעתיים")]
    [InlineData("he", 5, "לפני 5 שעות")]
    [InlineData("lt", 10, "prieš 10 valandų")]
    [InlineData("lt", 21, "prieš 21 valandą")]
    public void Hours_ago_read_as_CLDR_writes_them(string language, int hours, string said) =>
        Assert.Equal(said, RelativeTime.Ago(language, Now.AddHours(-hours), Now));

    [Fact]
    public void A_language_CLDR_lacks_says_it_in_English() =>
        Assert.Equal("3 hours ago", RelativeTime.Ago("xx", Now.AddHours(-3), Now));
}

[Part("strings")]
public class LocaleTests
{
    [Fact]
    public void Portuguese_formats_as_CLDR_s_pt_as_on_the_iPad() => Assert.Equal("pt", Locale.Culture("pt").Name);

    [Fact]
    public void Arabic_dates_are_Gregorian_with_Latin_digits()
    {
        var at = new DateTime(2026, 11, 22);
        Assert.IsType<System.Globalization.GregorianCalendar>(Locale.Culture("ar").DateTimeFormat.Calendar);
        Assert.Contains("2026", at.ToString("d MMMM yyyy", Locale.Culture("ar")));
    }

    [Theory]
    [InlineData("ar", true)]
    [InlineData("he", true)]
    [InlineData("fr", false)]
    [InlineData("ja", false)]
    public void Arabic_and_Hebrew_read_right_to_left(string language, bool rtl) => Assert.Equal(rtl, Locale.RightToLeft(language));

    /// <summary>Arabic's letters join, so its monospaced text is drawn in the plain face; Hebrew's don't.</summary>
    [Theory]
    [InlineData("ar", true)]
    [InlineData("ar-EG", true)]
    [InlineData("he", false)]
    [InlineData("en", false)]
    [InlineData("hi", false)]
    public void Arabic_letters_join(string language, bool joined) => Assert.Equal(joined, Locale.JoinedScript(language));
}

[Part("strings")]
public class IsolatedValuesTests
{
    [Fact]
    public void A_right_to_left_line_sets_each_value_apart_and_a_left_to_right_one_doesn_t()
    {
        // Ordinal: a culture's comparison ignores the isolates, which are what's checked here.
        Assert.Contains("⁨5⁩", WordBook.Load(SharedFiles.Strings, "ar").Get("needs_you.tab_label", ("n", "5")), StringComparison.Ordinal);
        Assert.Contains("⁨5⁩", WordBook.Load(SharedFiles.Strings, "he").Get("needs_you.tab_label", ("n", "5")), StringComparison.Ordinal);
        Assert.DoesNotContain("⁨", WordBook.Load(SharedFiles.Strings, "fr").Get("needs_you.tab_label", ("n", "5")), StringComparison.Ordinal);
    }
}

[Part("strings")]
public class WithoutDirectionTests
{
    [Fact]
    public void A_value_can_t_close_its_isolate_or_turn_the_line() =>
        Assert.Equal("⁨ab⁩", Locale.Isolated("a⁩‮b‏"), StringComparer.Ordinal);
}
