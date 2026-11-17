using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Tafs.Core.Tests.Ci;

/// <summary>
/// The setup's words (installer/words, docs/install.md): English and the app's 7 other languages, each a full copy of WiX's
/// standard setup lines. A missing id would show a raw #(loc.…) on a screen; a lost placeholder, link or key shortcut would
/// show a broken line. And each is in the setup under the LCIDs Windows asks for.
/// </summary>
[Part("ci")]
public class SetupWordsTests
{
    static readonly XNamespace Wxl = "http://wixtoolset.org/schemas/v4/wxl";

    /// <summary>Each language: its culture and LCID, and the LCIDs the setup holds it under.</summary>
    static readonly Dictionary<string, (string Culture, int Lcid, int[] InSetup)> Each = new()
    {
        ["de"] = ("de-de", 1031, [1031]),
        ["es"] = ("es-es", 3082, [3082, 1034]),
        ["fr"] = ("fr-fr", 1036, [1036]),
        ["it"] = ("it-it", 1040, [1040]),
        ["nl"] = ("nl-nl", 1043, [1043]),
        ["pl"] = ("pl-pl", 1045, [1045]),
        ["pt"] = ("pt-pt", 2070, [2070, 1046]),
    };

    public static IEnumerable<object[]> Languages => Each.Keys.Select(k => new object[] { k });

    static string WordsFile(string lang) => Path.Combine(RepoFiles.Root, "installer", "words", lang + ".wxl");

    static XElement Root(string lang) => XDocument.Load(WordsFile(lang)).Root!;

    static List<(string Id, string Value)> Lines(string lang) =>
        Root(lang).Elements(Wxl + "String").Select(s => ((string)s.Attribute("Id")!, (string)s.Attribute("Value")!)).ToList();

    [Fact]
    public void English_has_every_one_of_WiXs_lines_once()
    {
        var ids = Lines("en").Select(l => l.Id).ToList();
        Assert.Equal(64, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Each_language_says_which_it_is(string lang)
    {
        var root = Root(lang);
        Assert.Equal(Each[lang].Culture, (string?)root.Attribute("Culture"));
        Assert.Equal(Each[lang].Lcid.ToString(), (string?)root.Attribute("Language"));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Each_language_has_exactly_Englishs_ids(string lang) =>
        Assert.Equal(Lines("en").Select(l => l.Id), Lines(lang).Select(l => l.Id));

    /// <summary>
    /// Each line keeps English's placeholders ([WixBundleName]…), its link's markup, and as many key shortcuts (&amp;) as
    /// English: one on a button, none on a heading.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Each_line_keeps_Englishs_placeholders_links_and_shortcuts(string lang)
    {
        var english = Lines("en").ToDictionary(l => l.Id, l => l.Value);
        var wrong = new List<string>();
        foreach (var (id, value) in Lines(lang))
        {
            var en = english[id];
            if (!Placeholders(value).SequenceEqual(Placeholders(en))) wrong.Add($"{id}: placeholders");
            if (Count(value, "<a href=\"#\">") != Count(en, "<a href=\"#\">") || Count(value, "</a>") != Count(en, "</a>")) wrong.Add($"{id}: link");
            if (Count(value, "&") != Count(en, "&")) wrong.Add($"{id}: shortcut");
            if (value.Trim().Length == 0) wrong.Add($"{id}: empty");
        }
        Assert.Empty(wrong);
    }

    /// <summary>A button holds about 12 characters (75 dialog units); a longer word would be cut. Radio buttons are lines.</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Buttons_fit(string lang)
    {
        var tooLong = Lines(lang)
            .Where(l => l.Id.EndsWith("Button") && !l.Id.EndsWith("RadioButton") && l.Id != "UpdateButton" && l.Value.Replace("&", "").Length > 14)
            .Select(l => $"{l.Id}: {l.Value}");
        Assert.Empty(tooLong);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void The_setup_holds_each_language_under_its_LCIDs(string lang)
    {
        var bundle = RepoFiles.Read("installer/Bundle.wxs");
        foreach (var lcid in Each[lang].InSetup)
            Assert.Contains($"Name=\"{lcid}\\thm.wxl\" SourceFile=\"$(Words)\\{lang}.wxl\"", bundle);
    }

    [Fact]
    public void English_is_the_fallback_and_there_is_no_Options_page()
    {
        var bundle = RepoFiles.Read("installer/Bundle.wxs");
        Assert.Contains("LocalizationFile=\"$(Words)\\en.wxl\"", bundle);
        Assert.Contains("SuppressOptionsUI=\"yes\"", bundle);
    }

    static IEnumerable<string> Placeholders(string value) =>
        Regex.Matches(value, @"\[[A-Za-z]+\]").Select(m => m.Value).Order(StringComparer.Ordinal);

    static int Count(string value, string what) => (value.Length - value.Replace(what, "").Length) / what.Length;
}
