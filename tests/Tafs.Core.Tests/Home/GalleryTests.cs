using System.Text.Json;
using Tafs.Core.Home;

namespace Tafs.Core.Tests.Home;

/// <summary>The jobs' gallery, as the iPad's TafsCloud.Gallery, RecipeGallerySheet, RecipeStrip and RecipeSheet read it.</summary>
[Part("home")]
public class GalleryTests
{
    // The iPad's own sample (ios/Tafs/Samples/gallery.json), with a sample run and a logo added.
    const string Sample = """
        {"categories":[{"id":"money","title":"Money","color":{"light":"#107758","dark":"#4CC38A"}},
                       {"id":"customers","title":"Customers","color":{"light":"#B0127A","dark":"nope"}},
                       {"id":"operations","title":"Operations"},
                       {"id":"empty","title":"Nothing in it"},
                       {"title":"No id"}],
         "recipes":[
          {"id":"refunds-within-a-limit","title":"Refunds within a limit","category":"money","headline":"No refund waits overnight.","line":"Answers refund requests and refunds what's within your limit.","say":"Handle refund requests.",
           "card":{"allows":["Refunds up to €150 each"],"holds":["A refund over €150"],"never":["A payout"]},"apps":[{"name":"Gmail","logo":"/v1/logos/0123456789abcdef0123456789abcdef"},{"name":"Stripe"},{"logo":"x"}],"connected":true,"running":true,
           "proved":"  Proved on a test shop. ","sample":"Refunded 3 orders."},
          {"id":"chase-overdue-invoices","title":"Chase overdue invoices","category":"money","line":"Reminds customers about invoices past due, politely.","say":"Chase my overdue invoices.","apps":[{"name":"Qonto"}]},
          {"id":"answer-questions-about-orders","title":"Answer questions about orders","category":"customers","headline":"Stop answering \"where is my order?\"","card":{"allows":["Reply with tracking"]}},
          {"id":"weekly-team-update","title":"Weekly team update","category":"operations","headline":"Monday's update, written for you."},
          {"title":"No id"}],
         "strip":{"shown":true,"title":"More jobs to hand off","ids":["chase-overdue-invoices","gone","answer-questions-about-orders"]},
         "words":{"title":"New taf","all":"All jobs","own_words":"Say it in your own words","explore":"Explore jobs","see_all":"See all jobs","try":"Try it with Coach","sample":"What it reported after its test run"},
         "labels":{"allows":"Does on its own","holds":"Asks you first","never":"Never does","hand":"Hand it to Coach"}}
        """;

    static Gallery Read(string json) => Gallery.Read(JsonDocument.Parse(json).RootElement)!;

    [Fact]
    public void It_reads_leniently_leaving_out_what_it_can_t_read()
    {
        var g = Read(Sample);
        Assert.Equal(["money", "customers", "operations", "empty"], g.Categories.Select(c => c.Id));
        Assert.Equal(new RecipeTint(0x107758, 0x4CC38A), g.Categories[0].Color);
        Assert.Null(g.Categories[1].Color);
        Assert.Equal(4, g.Recipes.Count);
        var refunds = g.Recipes[0];
        Assert.Equal(["Gmail", "Stripe"], refunds.Apps.Select(a => a.Name));
        Assert.Equal(("Proved on a test shop.", "Refunded 3 orders.", true, true), (refunds.Proved, refunds.Sample, refunds.Connected, refunds.Running));
        Assert.Null(Gallery.Read(JsonDocument.Parse("[]").RootElement));
    }

    [Fact]
    public void A_job_is_led_by_the_problem_it_ends_else_its_title()
    {
        var g = Read(Sample);
        Assert.Equal("No refund waits overnight.", g.Recipes[0].Lead);
        Assert.Equal("Chase overdue invoices", g.Recipes[1].Lead);
    }

    [Fact]
    public void The_strip_keeps_tafs_cloud_s_order_and_skips_what_isn_t_listed()
    {
        var g = Read(Sample);
        Assert.Equal(["chase-overdue-invoices", "answer-questions-about-orders"], g.StripRecipes.Select(r => r.Id));
        Assert.Equal("See all jobs", g.SeeAll);
        Assert.Empty(Read("""{"recipes":[{"id":"a","title":"A"}],"strip":{"shown":false,"ids":["a"]}}""").StripRecipes);
        // The strip already shows every job: no row to open them all.
        Assert.Null(Read("""{"recipes":[{"id":"a","title":"A"}],"strip":{"shown":true,"ids":["a"]},"words":{"see_all":"See all"}}""").SeeAll);
        // A tafs cloud from before "see_all": its heading for every job.
        Assert.Equal("All jobs", Read("""{"recipes":[{"id":"a","title":"A"},{"id":"b","title":"B"}],"strip":{"shown":true,"ids":["a"]},"words":{"all":"All jobs"}}""").SeeAll);
    }

    [Fact]
    public void Its_words_are_tafs_cloud_s_and_none_means_no_row_or_button()
    {
        var g = Read(Sample);
        Assert.Equal("Explore jobs", g.Explore);
        Assert.Equal("Explore jobs", g.Title(browse: true));
        Assert.Equal("New taf", g.Title(browse: false));
        Assert.Equal("Try it with Coach", g.HandWords(browse: true));
        Assert.Equal("Hand it to Coach", g.HandWords(browse: false));
        Assert.Equal("What it reported after its test run", g.SampleLabel);
        var bare = Read("""{"recipes":[{"id":"a","title":"A"}]}""");
        Assert.Equal((null, null, null), (bare.Explore, bare.HandWords(browse: true), bare.SampleLabel));
        Assert.Null(Read("""{"recipes":[],"words":{"explore":"Explore jobs"}}""").Explore);
    }

    [Fact]
    public void Jobs_group_by_category_and_a_chip_narrows_them()
    {
        var g = Read(Sample);
        Assert.Equal(["money", "customers", "operations"], g.ByCategory.Select(x => x.Category.Id));
        Assert.Equal(["customers"], g.Groups("customers").Select(x => x.Category.Id));
        Assert.Equal(3, g.Groups("gone").Count);
        Assert.Equal(3, g.Groups(null).Count);
    }

    [Fact]
    public void A_job_s_sheet_has_its_card_s_zones_in_order_leaving_out_empty_ones()
    {
        var g = Read(Sample);
        Assert.Equal([("allows", "Does on its own"), ("holds", "Asks you first"), ("never", "Never does")],
            g.Zones(g.Recipes[0]).Select(z => (z.Id, z.Title)));
        Assert.Equal(["allows"], g.Zones(g.Recipes[2]).Select(z => z.Id));
        Assert.Empty(g.Zones(g.Recipes[3]));
    }

    [Theory]
    [InlineData("/v1/logos/0123456789abcdef0123456789abcdef", true)]
    [InlineData("/v1/logos/0123456789ABCDEF0123456789abcdef", false)]
    [InlineData("/v1/logos/0123456789abcdef", false)]
    [InlineData("https://example.com/v1/logos/0123456789abcdef0123456789abcdef", false)]
    [InlineData("/v1/other/0123456789abcdef0123456789abcdef", false)]
    [InlineData(null, false)]
    public void A_logo_loads_only_from_tafs_cloud_s_logos(string? path, bool loads) => Assert.Equal(loads, Gallery.IsLogo(path));

    /// <summary>
    /// Home's recipes with no taf (GET /v1/recipes, the iPad's homeListsRecipes): Home's line and own words from <c>home</c>,
    /// listed only when tafs cloud shows them, the sheet's words came, and a category has a job.
    /// </summary>
    [Fact]
    public void Home_s_recipes_are_listed_only_when_tafs_cloud_shows_them()
    {
        const string Answer = """
            {"shown": true, "categories": [{"id": "money", "title": "Money"}],
             "recipes": [{"id": "r", "title": "Refunds", "category": "money"}],
             "home": {"shown": true, "words": "Pick a job for your first taf.", "own_words": {"title": "Say it in your own words", "line": "Coach builds the taf with you."}},
             "labels": {"allows": "Does on its own", "holds": "Asks you first", "never": "Never does", "hand": "Hand it to Coach"}}
            """;
        var (jobs, lists) = Gallery.ReadForHome(JsonDocument.Parse(Answer).RootElement)!.Value;
        Assert.True(lists);
        Assert.Equal(("Pick a job for your first taf.", "Say it in your own words", "Coach builds the taf with you."), (jobs.Words.Title, jobs.Words.OwnWords, jobs.Words.OwnWordsLine));
        Assert.Equal("Hand it to Coach", jobs.HandWords(browse: false));
        Assert.False(Gallery.ReadForHome(JsonDocument.Parse(Answer.Replace("\"home\": {\"shown\": true", "\"home\": {\"shown\": false")).RootElement)!.Value.Lists);
        Assert.False(Gallery.ReadForHome(JsonDocument.Parse("""{"home": {"shown": true}, "labels": {"hand": "x"}}""").RootElement)!.Value.Lists);
        Assert.False(Gallery.ReadForHome(JsonDocument.Parse("""{"home": {"shown": true}, "categories": [{"id": "m", "title": "M"}], "recipes": [{"id": "r", "title": "R", "category": "m"}]}""").RootElement)!.Value.Lists);
    }
}
