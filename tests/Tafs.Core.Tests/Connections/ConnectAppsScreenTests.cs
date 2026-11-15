using Tafs.Core.Connections;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Connections;

[Part("connections")]
public class ConnectAppsScreenTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    public static IEnumerable<object[]> Shipped() => WordBook.Shipped(SharedFiles.Strings).Select(l => new object[] { l });

    [Fact]
    public void The_sample_reads_as_the_iPad_s_two_yours_and_four_to_discover()
    {
        var list = ConnectAppsScreen.Sample();
        var (yours, discover) = ConnectAppsScreen.Split(list.Apps);
        Assert.Equal(["Gmail", "Stripe"], yours.Select(a => a.DisplayName));
        Assert.Equal(["Google Sheets", "Slack", "Qonto", "HubSpot"], discover.Select(a => a.DisplayName));
        Assert.True(list.ByKey);
    }

    [Fact]
    public void A_row_says_how_whose_where_it_stands_and_who_uses_it()
    {
        // As the iPad's sample shows it (the reference set's connect-apps.png): no "Used by" there, so from tafs cloud's used_by here.
        var stripe = ConnectAppsScreen.Read("""
            {"apps":[{"entry":"stripe@3","vendor":"Stripe","method":"key","state":"connected","whose":"Yours","used_by":[{"name":"Refunds"},{"name":"Chargebacks"}]}]}
            """).Apps[0];
        Assert.Null(ConnectAppsScreen.Row(En, ConnectAppsScreen.Sample().Apps[1]).UsedBy);
        var row = ConnectAppsScreen.Row(En, stripe);
        Assert.Equal(new ConnectRow("With a key", "Yours", "Connected", "Used by Refunds and Chargebacks", null), row);
        Assert.Null(ConnectAppsScreen.Button(En, stripe));

        var slack = ConnectAppsScreen.Sample().Apps[3];
        Assert.Equal(new ConnectRow("Sign in", null, null, null, null), ConnectAppsScreen.Row(En, slack));
        Assert.Equal("Connect", ConnectAppsScreen.Button(En, slack));
    }

    [Fact]
    public void How_it_connects_is_method_where_sent_else_how()
    {
        var list = ConnectAppsScreen.Read("""
            {"apps":[{"entry":"a","how":"oauth","method":"key"},{"entry":"b","how":"key"},{"entry":"c"}]}
            """);
        Assert.Equal([true, true, false], list.Apps.Select(a => a.TakesKey));
    }

    [Fact]
    public void Reconnect_setup_and_signed_in()
    {
        var list = ConnectAppsScreen.Read("""
            {"apps":[
             {"entry":"x","name":"X","state":"needs_reconnect","used_by":[{"taf":"t1","name":"Refunds"}]},
             {"entry":"y","name":"Y","state":"needs_setup","words":"  Not set up yet  "},
             {"entry":"z","name":"Z","state":"signed_in"}
            ]}
            """);
        var (x, y, z) = (list.Apps[0], list.Apps[1], list.Apps[2]);
        Assert.Equal(new ConnectRow("Sign in", null, "Sign in again", "Used by Refunds", null), ConnectAppsScreen.Row(En, x));
        Assert.Equal("Sign in again", ConnectAppsScreen.Button(En, x));
        Assert.Equal("Not set up yet", ConnectAppsScreen.Row(En, y).Setup);
        Assert.Null(ConnectAppsScreen.Button(En, y));
        Assert.True(ConnectAppsScreen.NameIsSoft(y)); // the name in the soft colour, as on the iPad
        Assert.False(ConnectAppsScreen.NameIsSoft(x));
        Assert.True(z.Yours);
        Assert.False(y.Yours);
        // Used by, only for the person's own.
        Assert.Null(ConnectAppsScreen.Row(En, ConnectAppsScreen.Read("""{"apps":[{"entry":"a","used_by":[{"name":"R"}]}]}""").Apps[0]).UsedBy);
    }

    [Fact]
    public void An_app_under_Yours_isn_t_under_Discover_in_another_form()
    {
        var list = ConnectAppsScreen.Read("""
            {"apps":[
             {"entry":"connector:composio:stripe","vendor":"Stripe","state":"connected"},
             {"entry":"stripe@3","vendor":"Stripe"},
             {"entry":"google_sheets@2","vendor":"Google Sheets"},
             {"entry":"qonto@1","vendor":"Qonto"}
            ]}
            """);
        var (yours, discover) = ConnectAppsScreen.Split(list.Apps);
        Assert.Equal(["connector:composio:stripe"], yours.Select(a => a.Entry));
        Assert.Equal(["google_sheets@2", "qonto@1"], discover.Select(a => a.Entry));
        Assert.Equal(["googlesheets"], ConnectAppsScreen.Keys(list.Apps[2]));
    }

    [Fact]
    public void A_bad_entry_is_left_out_never_the_list()
    {
        var list = ConnectAppsScreen.Read("""
            {"apps":[{"name":"no entry"},7,{"entry":"   "},{"entry":"ok","name":"  ","title":"Ok title","used_by":"Refunds"}],"next_cursor":"c2"}
            """);
        var app = Assert.Single(list.Apps);
        Assert.Equal("Ok title", app.DisplayName);
        Assert.Null(app.UsedBy);
        Assert.Equal("c2", list.NextCursor);
        Assert.False(list.ByKey);
    }

    [Theory]
    [InlineData(960)]
    [InlineData(1200)]
    public void Wide_enough_the_three_keep_their_width_and_App_takes_the_rest(double total) =>
        Assert.Equal([total - 36 - 510, 150, 130, 230], ConnectAppsScreen.Widths(total));

    [Fact]
    public void Narrower_the_other_three_give_way_down_to_their_least()
    {
        var at = ConnectAppsScreen.Widths(700);
        Assert.Equal(ConnectAppsScreen.AppLeast, at[0], 6);
        Assert.Equal(700 - 36, at.Sum(), 6);
        var tight = ConnectAppsScreen.Widths(500);
        Assert.Equal([96.0, 84, 180], tight.Skip(1));
        Assert.Equal(500 - 36 - 360, tight[0], 6);
    }

    [Theory]
    [MemberData(nameof(Shipped))]
    public void Every_line_is_filled_in_every_shipped_language(string language)
    {
        var w = WordBook.Load(SharedFiles.Strings, language);
        var lines = ConnectAppsScreen.Sample().Apps.SelectMany(a =>
        {
            var r = ConnectAppsScreen.Row(w, a);
            return new[] { r.How, r.Status, r.UsedBy, ConnectAppsScreen.Button(w, a) };
        }).Concat(new[] { "connect.apps.title", "connect.apps.yours", "connect.apps.discover", "connect.apps.not_listed",
            "connect.apps.column_app", "connect.apps.column_how", "connect.apps.column_whose", "connect.apps.column_status" }.Select(k => w.Get(k)));
        Assert.All(lines.OfType<string>(), l => Assert.DoesNotContain("{", l));
    }

    [Fact]
    public void Make_one_first_shows_over_Discover_while_no_taf_can_have_an_app()
    {
        Assert.True(ConnectAppsScreen.SaysMakeATaf(4));
        Assert.False(ConnectAppsScreen.SaysMakeATaf(0));
        Assert.False(ConnectAppsScreen.SaysMakeATaf(4, forTaf: true));
        Assert.False(ConnectAppsScreen.SaysMakeATaf(4, connectableTafs: 1));
        Assert.DoesNotContain("{", En.Get("connect.apps_connect_taf_make_one"));
    }

    /// <summary>As the iPad's connectApps: what was typed, then a next page's catalog and cursor, each escaped whole.</summary>
    [Fact]
    public void A_page_is_read_with_its_search_catalog_and_cursor()
    {
        Assert.Equal("/v1/connect/apps", ConnectAppsScreen.Path());
        Assert.Equal("/v1/connect/apps", ConnectAppsScreen.Path(" \t "));
        Assert.Equal("/v1/connect/apps?q=google%20sheets", ConnectAppsScreen.Path("  google sheets "));
        Assert.Equal("/v1/connect/apps?q=a%26b&provider=composio&cursor=x%2By%2Fz%3D", ConnectAppsScreen.Path("a&b", "composio", "x+y/z="));
    }

    /// <summary>As the iPad's loadMoreConnectApps: the next page under those shown, no app twice, Coach's offer kept.</summary>
    [Fact]
    public void More_apps_adds_the_next_page_without_repeats()
    {
        var shown = ConnectAppsScreen.Read("""{"apps": [{"entry": "stripe@3", "title": "Stripe"}], "by_key": true, "next_cursor": "c1", "provider": "composio"}""");
        var page = ConnectAppsScreen.Read("""{"apps": [{"entry": "stripe@3", "title": "Stripe"}, {"entry": "slack@1", "title": "Slack", "logo": "/v1/logos/0123456789abcdef0123456789abcdef"}]}""");

        var merged = ConnectAppsScreen.More(shown, page);

        Assert.Equal(["stripe@3", "slack@1"], merged.Apps.Select(a => a.Entry));
        Assert.True(merged.ByKey);
        Assert.Null(merged.NextCursor);
        Assert.Equal(("c1", "composio"), (shown.NextCursor, shown.Provider));
        Assert.Equal("/v1/logos/0123456789abcdef0123456789abcdef", merged.Apps[1].Logo);
    }

    /// <summary>As the iPad's logoURL: only tafs cloud's own logos, by a 32-character lowercase hex key.</summary>
    [Theory]
    [InlineData("/v1/logos/0123456789abcdef0123456789abcdef", "https://cloud.tafs.kiff.dev/v1/logos/0123456789abcdef0123456789abcdef")]
    [InlineData("/v1/logos/0123456789ABCDEF0123456789abcdef", null)]
    [InlineData("/v1/logos/0123", null)]
    [InlineData("https://evil.example/v1/logos/0123456789abcdef0123456789abcdef", null)]
    [InlineData("/v1/logos/0123456789abcdef0123456789abcdef/..", null)]
    [InlineData(null, null)]
    public void A_logo_comes_only_from_tafs_cloud(string? path, string? url) => Assert.Equal(url, ConnectAppsScreen.LogoUrl(path)?.AbsoluteUri);
}
