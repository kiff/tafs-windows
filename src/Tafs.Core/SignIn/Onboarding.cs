using System.Text.Json;
using Tafs.Core.Words;

namespace Tafs.Core.SignIn;

/// <summary>
/// The first run (kiff/tafs <c>docs/onboarding.md</c>, the iPad's <c>Onboarding</c>, tafs-linux's <c>onboarding</c>): the
/// welcome with one tick for both Terms, the privacy links and the age line; KIFF's sign-in in the browser; and the
/// three cards on how tafs works, once on this PC.
/// </summary>
public static class Onboarding
{
    /// <summary>The age every country reads (<c>MinimumAge.standard</c>).</summary>
    public const int StandardAge = 16;

    /// <summary>The three cards: what a taf is, a Card, and staying in control.</summary>
    public enum Card { Tafs, Card, Control }

    /// <summary>
    /// The welcome's age line for the person's country: its own age where the law asks more than 16, else the line every
    /// country reads (<c>Onboarding.ageLine</c>).
    /// </summary>
    public static string AgeLine(WordBook w, MinimumAge ages, string? region)
    {
        var age = ages.Of(region);
        return age > StandardAge ? w.Get("onboarding.age_n", ("age", age.ToString(Locale.Culture(w.Language)))) : w.Get("onboarding.age");
    }

    /// <summary>Whether the three cards follow the celebration: once on this PC, until Get started or Skip.</summary>
    public static bool ShowsCards(bool seen) => !seen;

    /// <summary>
    /// The one tick: "I agree to KIFF's Terms of Service and the tafs Terms", in pieces, each name linked wherever the
    /// language puts it (KIFF's Terms, then tafs's).
    /// </summary>
    public static IReadOnlyList<(string Text, Uri? Link)> ConsentTick(WordBook w) =>
        Linked(w.Get("onboarding.consent_tick", ("kiff_terms", "\u0001"), ("tafs_terms", "\u0002")),
            (w.Get("onboarding.kiff_terms"), KiffPage(w.Language, "terms")), (w.Get("onboarding.tafs_terms"), SignInScreen.Terms(w.Language)));

    /// <summary>The tick's words for a screen reader, the names in place.</summary>
    public static string ConsentTickSaid(WordBook w) =>
        w.Get("onboarding.consent_tick", ("kiff_terms", w.Get("onboarding.kiff_terms")), ("tafs_terms", w.Get("onboarding.tafs_terms")));

    /// <summary>Privacy, as two links, never ticked: KIFF's, then tafs's.</summary>
    public static IReadOnlyList<(string Text, Uri? Link)> PrivacyLinks(WordBook w) =>
        Linked(w.Get("onboarding.privacy_links", ("kiff_privacy", "\u0001"), ("tafs_privacy", "\u0002")),
            (w.Get("onboarding.kiff_privacy"), KiffPage(w.Language, "privacy")), (w.Get("onboarding.tafs_privacy"), SignInScreen.Privacy(w.Language)));

    /// <summary>
    /// The last card's lead, naming how this PC approves: on the phone, or with Windows Hello once paired
    /// (<c>onboarding.control_lead_desktop</c>, every desktop's).
    /// </summary>
    public static string ControlLead(WordBook w) => w.Get("onboarding.control_lead_desktop");

    /// <summary>
    /// KIFF's own Terms or Privacy (<c>Legal.kiffPage</c>): in the app's language where kiff.dev has it, under KIFF's code,
    /// else in English at the unprefixed path, never <c>/en/</c>.
    /// </summary>
    public static Uri KiffPage(string language, string page) =>
        new(SignInScreen.LegalCode(language) is { } code ? $"https://kiff.dev/{code}/{page}" : $"https://kiff.dev/{page}");

    /// <summary>A line with two markers, \u0001 and \u0002, split into its words and its two links.</summary>
    static IReadOnlyList<(string Text, Uri? Link)> Linked(string line, (string Text, Uri Link) first, (string Text, Uri Link) second)
    {
        var parts = new List<(string, Uri?)>();
        var text = "";
        foreach (var c in line)
        {
            if (c is '\u0001' or '\u0002')
            {
                if (text.Length > 0) parts.Add((text, null));
                text = "";
                var (t, l) = c == '\u0001' ? first : second;
                parts.Add((t, l));
            }
            else text += c;
        }
        if (text.Length > 0) parts.Add((text, null));
        return parts;
    }
}

/// <summary>
/// The minimum age to use the app by country (ISO 3166-1): legal-i18n's table, <c>app/strings/ages.json</c> at the pin,
/// which the iPad's <c>MinimumAge</c> is generated from. Any other country, or none known, is the table's default.
/// </summary>
public sealed class MinimumAge
{
    readonly int defaultAge;
    readonly IReadOnlyDictionary<string, int> byRegion;

    MinimumAge(int defaultAge, IReadOnlyDictionary<string, int> byRegion) => (this.defaultAge, this.byRegion) = (defaultAge, byRegion);

    /// <summary>The table beside the languages; the standard age everywhere when it can't be read.</summary>
    public static MinimumAge Load(string stringsRoot)
    {
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(Path.Combine(stringsRoot, "ages.json")));
            if (map is not null && map.TryGetValue("default", out var d))
                return new MinimumAge(d, map.Where(p => p.Key != "default").ToDictionary(p => p.Key.ToUpperInvariant(), p => p.Value));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return new MinimumAge(Onboarding.StandardAge, new Dictionary<string, int>());
    }

    /// <summary>The age in a region ("IN"), or the default where the table has none or no region is known.</summary>
    public int Of(string? region) =>
        region is { Length: 2 } r && byRegion.TryGetValue(r.ToUpperInvariant(), out var age) ? age : defaultAge;
}

/// <summary>The welcome's one tick (<c>ConsentTick</c>): Continue and the phone's code wait on it. The sample never waits.</summary>
public sealed record ConsentTick(bool Agreed = false)
{
    public ConsentTick Toggled() => this with { Agreed = !Agreed };
    public bool CanContinue => Agreed;
}
