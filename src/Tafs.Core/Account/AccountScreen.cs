using Tafs.Core.Kiff;
using Tafs.Core.SignIn;
using Tafs.Core.Words;

namespace Tafs.Core.Account;

/// <summary>Where this PC stands: the iPad's sample (screenshots only here), signed in with KIFF, or signed out.</summary>
public enum AccountMode { Sample, Kiff, SignedOut }

/// <summary>What an Account action does.</summary>
public enum AccountAction { SignIn, SignOut, ClearSample }

/// <summary>An Account action's row: its words, what it does, and whether it's red (it ends something).</summary>
public sealed record ActionRow(string Words, AccountAction Action, bool Destructive);

/// <summary>
/// Account, as on the iPad (its SettingsView): Your account, Alerts, Theme, Language, How it works, Legal, About and Account
/// actions, in that order. Every word is the word book's.
/// </summary>
public static class AccountScreen
{
    public static AccountMode ModeOf(bool sample, KeptSession? kept) =>
        sample ? AccountMode.Sample : kept is not null ? AccountMode.Kiff : AccountMode.SignedOut;

    /// <summary>Mode's value: Sample data, Signed in with KIFF, or Signed out.</summary>
    public static string ModeWords(WordBook w, AccountMode mode) => w.Get(mode switch
    {
        AccountMode.Sample => "settings.mode_sample",
        AccountMode.Kiff => "settings.mode_kiff",
        _ => "settings.mode_signed_out",
    });

    /// <summary>View only exactly when the session says view (its approval_mode, unknown counting as view).</summary>
    public static bool ViewOnly(AccountMode mode, KeptSession? kept) => mode == AccountMode.Kiff && kept?.Mode == ApprovalMode.View;

    /// <summary>
    /// The actions, as on the iPad: over the sample, Sign in with KIFF and Clear the sample; signed in, Sign out (red, asked
    /// first); signed out, Sign in with KIFF.
    /// </summary>
    public static IReadOnlyList<ActionRow> Actions(WordBook w, AccountMode mode) => mode switch
    {
        AccountMode.Sample => [new(w.Get("sign_in.with_kiff"), AccountAction.SignIn, false), new(w.Get("settings.clear_sample"), AccountAction.ClearSample, true)],
        AccountMode.Kiff => [new(w.Get("settings.sign_out"), AccountAction.SignOut, true)],
        _ => [new(w.Get("sign_in.with_kiff"), AccountAction.SignIn, false)],
    };

    /// <summary>A legal row's label. kiff.dev has tafs's legal pages in the app's languages; in one it doesn't, the row says
    /// they open in English ("Terms (in English)").</summary>
    public static string Legal(WordBook w, string key) => LegalTitle(w, w.Get(key));

    /// <summary>A legal row's title as given (How tafs works' <c>onboarding.row_*</c>), saying it opens in English where kiff.dev lacks the language.</summary>
    public static string LegalTitle(WordBook w, string title) =>
        w.Language == "en" || SignInScreen.LegalCode(w.Language) is not null
            ? title
            : w.Get("settings.legal_in_english", ("label", title));

    /// <summary>How it works, for this Windows PC.</summary>
    public static string HowItWorks(WordBook w) => w.Get("sign_in.every_action_taf_takes_goes", ("device", w.Get("device.windows")));

    /// <summary>Sign out's question: its title and what it does to this Windows PC.</summary>
    public static (string Title, string Body) SignOutQuestion(WordBook w) =>
        (w.Get("settings.sign_out_of_kiff.title"), w.Get("settings.sign_out_of_kiff.body_device", ("device", w.Get("device.windows"))));

    /// <summary>A language by its own name, as the iPad's Language row shows it: "English", "Français", "Português".</summary>
    public static string LanguageName(string language)
    {
        var culture = new System.Globalization.CultureInfo(language == "pt" ? "pt-PT" : language);
        var name = culture.Parent.Name.Length > 0 ? culture.Parent.NativeName : culture.NativeName;
        return name.Length > 0 ? char.ToUpper(name[0], culture) + name[1..] : language;
    }

    /// <summary>The version as the iPad shows it: "1.0 (1)", or just the version with no build.</summary>
    public static string VersionLabel(Version? v) => v is null ? "–" : v.Build > 0 ? $"{v.Major}.{v.Minor} ({v.Build})" : $"{v.Major}.{v.Minor}";
}
