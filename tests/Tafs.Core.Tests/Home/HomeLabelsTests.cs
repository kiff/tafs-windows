using System.Text.Json;
using Tafs.Core.Home;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Home;

/// <summary>Labels on the tafs list, as kiff/tafs docs/labels.md and the iPad's LabelChips and AppModel.filterList.</summary>
[Part("home")]
public class HomeLabelsTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static HomeAnswer Home(string json) => HomeList.Read(En, JsonDocument.Parse(json).RootElement, TimeZoneInfo.Utc);

    const string Labelled = """
        {"tafs":[
          {"id":"t1","name":"Rae","state":"working","labels":["l_money"]},
          {"id":"t2","name":"Ana","state":"waiting","labels":["l_cust","l_money","l_ops"]},
          {"id":"t3","name":"Bo","state":"idle"}],
         "labels":[
          {"id":"l_money","name":"Money","color":"money","count":2,"waiting":1,"headline":{"words":"2 tafs working · Money"}},
          {"id":"l_cust","name":"Customers","color":"customers","count":1,"waiting":true},
          {"name":"No id"},
          "not a label",
          {"id":"l_ops","name":"Ops","color":"operations","count":1,"waiting":false}],
         "labels_words":{"all":"Tout"}}
        """;

    /// <summary>Home's labels read as sent, in order; one without an id, or of another shape, is left out; waiting yes/no reads as one.</summary>
    [Fact]
    public void Home_s_labels_read_as_sent_in_their_order()
    {
        var home = Home(Labelled);
        Assert.Equal(
            [new HomeLabel("l_money", "Money", "money", 2, 1, "2 tafs working · Money"),
             new HomeLabel("l_cust", "Customers", "customers", 1, 1, null),
             new HomeLabel("l_ops", "Ops", "operations", 1, 0, null)],
            home.Labels.Labels);
        Assert.Equal("Tout", LabelChips.All(En, home.Labels));
        Assert.Equal(["l_cust", "l_money", "l_ops"], home.Tafs[1].Labels);
        Assert.Empty(home.Tafs[2].Labels);
    }

    /// <summary>With no labelled taf Home sends none: no chips, and the first chip would be the app's own word.</summary>
    [Fact]
    public void No_labels_with_no_labelled_taf_and_another_shape_reads_as_none()
    {
        var plain = Home("""{"tafs":[{"id":"t1","name":"Rae","state":"working"}]}""");
        Assert.Empty(plain.Labels.Labels);
        // Home's own word for it, never the Record's filter's: they differ in some languages.
        Assert.Equal(En.Get("home.labels_all"), LabelChips.All(En, plain.Labels));
        Assert.Equal("Tous", LabelChips.All(WordBook.Load(SharedFiles.Strings, "fr"), plain.Labels));

        var odd = Home("""{"tafs":[{"id":"t1","name":"Rae","state":"working","labels":"l_money"}],"labels":{"id":"l"},"labels_words":"All"}""");
        Assert.Single(odd.Tafs);
        Assert.Empty(odd.Labels.Labels);
        Assert.Empty(odd.Tafs[0].Labels);
        Assert.Null(odd.Labels.All);
    }

    /// <summary>A chip filters to its tafs in Home's order, never reordering; All, or a label that's gone, shows them all.</summary>
    [Fact]
    public void A_chip_filters_without_reordering_and_a_gone_label_shows_them_all()
    {
        var home = Home(Labelled);
        var rows = HomeList.Rows(En, home, TimeZoneInfo.Utc);
        IReadOnlyList<string> Keys(string? label) => LabelChips.Filtered(rows, r => r.LabelIds, label, home.Labels).Select(r => r.Key).ToList();

        Assert.Equal(["t1", "t2"], Keys("l_money"));
        Assert.Equal(["t2"], Keys("l_cust"));
        Assert.Equal(["t1", "t2", "t3"], Keys(null));
        Assert.Equal(["t1", "t2", "t3"], Keys("l_gone"));
        Assert.Null(LabelChips.Chosen("l_gone", home.Labels));
    }

    /// <summary>
    /// The chips' row: All, two labels, then "+N" until it's clicked. A taf's own chips follow the account's order: two
    /// names, then "+1".
    /// </summary>
    [Fact]
    public void The_row_shows_two_then_more_and_a_taf_s_chips_follow_the_account_s_order()
    {
        var home = Home(Labelled);
        var (shown, hidden) = LabelChips.Row(home.Labels, expanded: false);
        Assert.Equal(["l_money", "l_cust"], shown.Select(l => l.Id));
        Assert.Equal(1, hidden);
        var (all, none) = LabelChips.Row(home.Labels, expanded: true);
        Assert.Equal(3, all.Count);
        Assert.Equal(0, none);

        var rows = HomeList.Rows(En, home, TimeZoneInfo.Utc);
        Assert.Equal(["Money"], rows[0].Chips);
        // Ana carries Customers, Money and Ops; the account's order is Money, Customers, Ops.
        Assert.Equal(["Money", "Customers", "+1"], rows[1].Chips);
        Assert.Empty(rows[2].Chips!);
    }
}
