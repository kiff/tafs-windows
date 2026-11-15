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
        var stripe = ConnectAppsScreen.Sample().Apps[1];
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
        Assert.True(z.Yours);
        Assert.False(y.Yours);
        // Used by, only for the person's own.
        Assert.Null(ConnectAppsScreen.Row(En, ConnectAppsScreen.Read("""{"apps":[{"entry":"a","used_by":["R"]}]}""").Apps[0]).UsedBy);
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
}
