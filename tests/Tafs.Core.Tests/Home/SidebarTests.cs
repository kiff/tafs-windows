using Tafs.Core.Home;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Home;

[Part("home")]
public class SidebarTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    public static IEnumerable<object[]> Shipped() => WordBook.Shipped(SharedFiles.Strings).Select(l => new object[] { l });

    [Fact]
    public void The_mascot_opens_the_Overview_and_there_is_no_Overview_item_beside_it()
    {
        Assert.Equal(Screen.Overview, Sidebar.BrandOpens);
        Assert.DoesNotContain(Sidebar.Screens(En), i => i.Screen == Screen.Overview);
    }

    [Fact]
    public void The_screens_are_in_the_iPad_s_order()
    {
        Assert.Equal(new[] { Screen.NeedsYou, Screen.Record, Screen.Calendar, Screen.Usage, Screen.ConnectApps },
            Sidebar.Screens(En).Select(i => i.Screen));
        Assert.Equal(new[] { "Needs you", "Record", "Calendar" }, Sidebar.Screens(En).Take(3).Select(i => i.Title));
        Assert.All(Sidebar.Screens(En), i => Assert.Single(i.Glyph));
    }

    [Fact]
    public void Calendar_is_in_the_sidebar_so_the_Overview_draws_no_calendar_button()
    {
        Assert.Contains(Sidebar.Screens(En), i => i.Screen == Screen.Calendar);
        Assert.False(OverviewScreen.ShowsCalendar);
    }

    [Fact]
    public void Coach_and_Account_are_at_the_foot_not_among_the_screens()
    {
        Assert.DoesNotContain(Sidebar.Screens(En), i => i.Screen is Screen.Coach or Screen.Account);
        var foot = Sidebar.Foot(En, "maria@example.com");
        Assert.Equal(("Coach", "Account"), (foot.Coach, foot.Account));
        Assert.Equal("Account, maria@example.com", foot.AccountLabel);
        Assert.Equal("Account", Sidebar.Foot(En).AccountLabel);
    }

    [Fact]
    public void Needs_you_s_count_is_a_plain_number_and_names_the_row()
    {
        Assert.Equal(("2", "Needs you, 2"), Sidebar.NeedsYouCount(En, 2));
        Assert.Equal((null, "Needs you"), Sidebar.NeedsYouCount(En, 0));
    }

    [Theory]
    [InlineData("en", "YOUR TAFS")]
    [InlineData("fr", null)]
    public void A_section_label_is_in_capitals(string language, string? expected)
    {
        var w = WordBook.Load(SharedFiles.Strings, language);
        var label = Sidebar.Eyebrow(w, "home.your_tafs");
        if (expected is not null) Assert.Equal(expected, label);
        Assert.Equal(label, label.ToUpperInvariant());
    }

    [Fact]
    public void With_no_tafs_the_line_is_tafs_cloud_s_when_it_sends_one()
    {
        Assert.Equal("Connect an app to set up your first taf.", Sidebar.NoTafs(En));
        Assert.Equal("Tafs shared with you show here.", Sidebar.NoTafs(En, mayUseCoach: false));
        Assert.Equal("From the cloud", Sidebar.NoTafs(En, "From the cloud"));
    }

    [Fact]
    public void The_Overview_has_its_four_columns_in_order()
    {
        var lines = OverviewScreen.Lines(En);
        Assert.Equal("Overview", lines.Title);
        Assert.Equal(new[] { "TAF", "NOW", "CARD", "LAST RESULT" }, lines.Columns);
        Assert.Equal("No tafs yet.", lines.Empty);
    }

    [Theory]
    [MemberData(nameof(Shipped))]
    public void Every_line_is_there_in_every_shipped_language(string language)
    {
        var w = WordBook.Load(SharedFiles.Strings, language);
        var o = OverviewScreen.Lines(w);
        var foot = Sidebar.Foot(w, "a@example.com");
        var all = Sidebar.Screens(w).Select(i => i.Title)
            .Concat([Sidebar.Eyebrow(w, "home.your_tafs"), Sidebar.NoTafs(w), Sidebar.NoTafs(w, mayUseCoach: false)])
            .Concat([foot.Coach, foot.Account, foot.AccountLabel, w.Get("app.name")])
            .Concat([o.Title, o.Subtitle, o.Empty, o.Footnote]).Concat(o.Columns);
        Assert.All(all, line => { Assert.False(string.IsNullOrWhiteSpace(line)); Assert.DoesNotContain("{", line); });
        Assert.Contains("a@example.com", foot.AccountLabel);
    }
}
