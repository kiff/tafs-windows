using Tafs.Core.SignIn;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.SignIn;

[Part("pairing")]
public class OnboardingTests
{
    public static IEnumerable<object[]> Shipped() => WordBook.Shipped(SharedFiles.Strings).Select(l => new object[] { l });

    [Fact]
    public void The_age_table_is_legal_i18n_s()
    {
        var ages = MinimumAge.Load(SharedFiles.Strings);
        Assert.Equal(18, ages.Of("IN"));
        Assert.Equal(18, ages.Of("br"));
        Assert.Equal(16, ages.Of("FR"));
        Assert.Equal(16, ages.Of(null));
        Assert.Equal(16, ages.Of("France"));
        // No table: the standard age everywhere.
        Assert.Equal(16, MinimumAge.Load(Path.GetTempPath() + "no-such-folder").Of("IN"));
    }

    [Theory, MemberData(nameof(Shipped))]
    public void Every_shipped_language_has_the_welcome_s_lines(string language)
    {
        var w = WordBook.Load(SharedFiles.Strings, language);
        var ages = MinimumAge.Load(SharedFiles.Strings);
        Assert.NotEmpty(Onboarding.AgeLine(w, ages, "FR"));
        Assert.Contains("18", Onboarding.AgeLine(w, ages, "IN"));
        Assert.NotEmpty(Onboarding.ControlLead(w));
        Assert.NotEmpty(Onboarding.ConsentTickSaid(w));
        // Both Terms are linked in the tick, and both privacy pages below it.
        var tick = Onboarding.ConsentTick(w);
        Assert.Equal(2, tick.Count(p => p.Link is not null));
        Assert.Equal(2, Onboarding.PrivacyLinks(w).Count(p => p.Link is not null));
        Assert.DoesNotContain(tick, p => p.Text.Contains('\u0001') || p.Text.Contains('\u0002'));
    }

    [Fact]
    public void The_links_are_kiff_dev_s_in_the_app_s_language()
    {
        var w = WordBook.Load(SharedFiles.Strings, "fr");
        var links = Onboarding.ConsentTick(w).Where(p => p.Link is not null).Select(p => p.Link!.AbsoluteUri).ToList();
        Assert.Equal(["https://kiff.dev/fr/terms", "https://kiff.dev/fr/tafs/terms"], links);
        Assert.Equal("https://kiff.dev/zh/privacy", Onboarding.KiffPage("zh-Hant", "privacy").AbsoluteUri);
        Assert.Equal("https://kiff.dev/terms", Onboarding.KiffPage("en", "terms").AbsoluteUri);
        Assert.Equal("https://kiff.dev/terms", Onboarding.KiffPage("xx", "terms").AbsoluteUri);
    }

    [Fact]
    public void Continue_waits_on_the_tick_and_the_cards_show_once()
    {
        var tick = new ConsentTick();
        Assert.False(tick.CanContinue);
        Assert.True(tick.Toggled().CanContinue);
        Assert.False(tick.Toggled().Toggled().CanContinue);
        Assert.True(Onboarding.ShowsCards(seen: false));
        Assert.False(Onboarding.ShowsCards(seen: true));
    }
}

[Part("pairing")]
public class OnboardingCardsTests
{
    public static IEnumerable<object[]> Shipped() => WordBook.Shipped(SharedFiles.Strings).Select(l => new object[] { l });

    [Theory, MemberData(nameof(Shipped))]
    public void Every_card_reads_in_every_shipped_language(string language)
    {
        var w = WordBook.Load(SharedFiles.Strings, language);
        foreach (var card in Enum.GetValues<Onboarding.Card>())
        {
            Assert.NotEmpty(Onboarding.Title(w, card));
            Assert.NotEmpty(Onboarding.Lead(w, card));
            Assert.NotEmpty(Onboarding.Button(w, card));
        }
        Assert.Equal(3, Onboarding.SampleTafRows(w).Count);
        Assert.Equal(4, Onboarding.LegalRows(w).Count);
    }

    [Fact]
    public void The_cards_go_in_order_and_the_last_says_Get_started()
    {
        var w = WordBook.Load(SharedFiles.Strings, "en");
        Assert.Equal(Onboarding.Card.Card, Onboarding.Next(Onboarding.Card.Tafs));
        Assert.Equal(Onboarding.Card.Control, Onboarding.Next(Onboarding.Card.Card));
        Assert.Null(Onboarding.Next(Onboarding.Card.Control));
        Assert.Equal(w.Get("common.next"), Onboarding.Button(w, Onboarding.Card.Tafs));
        Assert.Equal(w.Get("onboarding.get_started"), Onboarding.Button(w, Onboarding.Card.Control));
        Assert.Equal(w.Get("onboarding.control_lead_desktop"), Onboarding.Lead(w, Onboarding.Card.Control));
        Assert.Equal(["Support replies", "Late parcels", "Lead follow-up"], Onboarding.SampleTafRows(w).Select(r => r.Name));
    }
}

[Part("pairing")]
public class FirstHomeTests
{
    public static IEnumerable<object[]> Shipped() => WordBook.Shipped(SharedFiles.Strings).Select(l => new object[] { l });

    [Theory, MemberData(nameof(Shipped))]
    public void The_first_Home_reads_in_every_shipped_language(string language)
    {
        var w = WordBook.Load(SharedFiles.Strings, language);
        var (title, lead) = Onboarding.FirstHomeLines(w, null);
        Assert.Equal(w.Get("home.no_tafs"), title);
        Assert.Equal(w.Get("onboarding.home_lead"), lead);
        Assert.NotEmpty(w.Get("coach.ask"));
        Assert.NotEmpty(w.Get("onboarding.how_it_works"));
    }

    [Fact]
    public void tafs_cloud_s_own_line_stands_alone_and_only_Coach_s_users_see_the_first_Home()
    {
        var w = WordBook.Load(SharedFiles.Strings, "en");
        Assert.Equal(("Nothing set up yet.", (string?)null), Onboarding.FirstHomeLines(w, "Nothing set up yet."));
        Assert.Equal(w.Get("home.no_tafs"), Onboarding.FirstHomeLines(w, "  ").Title);
        Assert.True(Onboarding.ShowsFirstHome(mayUseCoach: true));
        Assert.False(Onboarding.ShowsFirstHome(mayUseCoach: false));
    }
}
