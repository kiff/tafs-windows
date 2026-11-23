using Tafs.Core.Pairing;
using Tafs.Core.Words;

namespace Tafs.Core.SignIn;

/// <summary>Where the sign-in screen stands.</summary>
public enum SignInStep
{
    /// <summary>Before anything: what tafs is, Sign in with KIFF, or add this computer with the phone.</summary>
    Welcome,
    /// <summary>KIFF gave a code: the person checks it in the browser and approves.</summary>
    Waiting,
    CouldNotStart,
    Expired,
    CouldNotFinish,
}

/// <summary>The lines the sign-in screen shows at one step. A null line isn't shown.</summary>
public sealed record SignInLines(
    string Title,
    string? Lead,
    string? CodeLabel,
    string? Code,
    string? Status,
    string? Button,
    string? OtherBrowser,
    string? Link,
    string? PairInstead);

/// <summary>
/// The sign-in screen (desktop spec §4): every line from the word book, nothing on the network.
/// </summary>
public static class SignInScreen
{
    public static SignInLines Lines(WordBook w, SignInStep step, SignInCode? code = null, string? why = null)
    {
        var device = ("device", PairingScreen.Device(w));
        var title = w.Get("sign_in.welcome.title");
        var withKiff = w.Get("sign_in.with_kiff");
        var pair = w.Get("pair.from_sign_in", device);
        string Why() => why ?? throw new ArgumentNullException(nameof(why));
        return step switch
        {
            SignInStep.Welcome => new SignInLines(title, w.Get("sign_in.lead", device),
                null, null, null, withKiff, null, null, pair),
            // The link is shown too, for a browser other than the one that opened.
            SignInStep.Waiting => new SignInLines(title, w.Get("sign_in.check_code"),
                w.Get("sign_in.your_code"), Code(code).UserCode,
                w.Get("sign_in.waiting_for_browser", ("user_code", Code(code).UserCode), device),
                w.Get("sign_in.open_kiff"), w.Get("sign_in.other_browser"),
                Code(code).VerificationUriComplete.AbsoluteUri, null),
            SignInStep.CouldNotStart => new SignInLines(title, null, null, null,
                w.Get("sign_in.could_not_start", ("why", Why())), withKiff, null, null, pair),
            SignInStep.Expired => new SignInLines(title, null, null, null,
                w.Get("sign_in.relink.code_expired"), withKiff, null, null, pair),
            SignInStep.CouldNotFinish => new SignInLines(title, null, null, null,
                w.Get("sign_in.could_not_finish", ("why", Why())), withKiff, null, null, pair),
            _ => throw new ArgumentOutOfRangeException(nameof(step)),
        };
    }

    /// <summary>
    /// The languages kiff.dev has tafs's Terms and Privacy policy in, besides English, at
    /// <c>kiff.dev/&lt;lang&gt;/tafs/terms</c> and <c>/&lt;lang&gt;/tafs/privacy</c>, by KIFF's code (checked against kiff.dev,
    /// 22 Nov). The English pages govern.
    /// </summary>
    public static readonly IReadOnlySet<string> LegalLanguages = new HashSet<string>
    {
        "de", "es", "fr", "it", "nl", "pl", "pt", "ko", "ja", "et", "lt", "lv", "cs", "sk", "sl", "hu", "ro", "bg", "hr", "tr",
        "id", "vi", "th", "zh", "ar", "sr", "sq", "ka", "hy", "az", "bn", "he", "ca", "mk", "mn", "ne", "km", "lo", "el", "tk", "hi",
        // Brazilian Portuguese has its own pages, in lower case (kiff.dev answers 404 for /pt-BR/).
        "pt-br",
    };

    /// <summary>
    /// The code kiff.dev serves a language's legal pages under, if it has them: KIFF's code, so Traditional Chinese is "zh" and
    /// Latin Serbian "sr" (the iPad's <c>Legal.translated</c>). Brazilian Portuguese has its own pages, "pt-br", in lower case;
    /// plain Portuguese stays on "pt".
    /// </summary>
    public static string? LegalCode(string language)
    {
        var code = language switch { "zh-Hant" => "zh", "sr-Latn" => "sr", "pt-BR" => "pt-br", _ => language };
        return LegalLanguages.Contains(code) ? code : null;
    }

    /// <summary>tafs's own Terms, in the app's language where kiff.dev has it, else in English.</summary>
    public static Uri Terms(string language) => Legal(language, "terms");

    /// <summary>tafs's own Privacy policy, in the app's language where kiff.dev has it, else in English.</summary>
    public static Uri Privacy(string language) => Legal(language, "privacy");

    static Uri Legal(string language, string page) =>
        new(LegalCode(language) is { } code ? $"https://kiff.dev/{code}/tafs/{page}" : $"https://kiff.dev/tafs/{page}");

    /// <summary>"By signing in, you agree to …", in pieces: the two names linked wherever the language puts them.</summary>
    public static IReadOnlyList<(string Text, Uri? Link)> Agreement(WordBook w)
    {
        var line = w.Get("sign_in.agree", ("terms", "\u0001"), ("privacy", "\u0002"));
        var parts = new List<(string Text, Uri? Link)>();
        var start = 0;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] is not ('\u0001' or '\u0002')) continue;
            if (i > start) parts.Add((line[start..i], null));
            parts.Add(line[i] == '\u0001' ? (w.Get("settings.terms"), Terms(w.Language)) : (w.Get("settings.privacy_policy"), Privacy(w.Language)));
            start = i + 1;
        }
        if (start < line.Length) parts.Add((line[start..], null));
        return parts;
    }

    /// <summary>Where a poll's answer leaves the screen. Null: signed in, the screen closes.</summary>
    public static SignInStep? After(SignInStatus status) => status switch
    {
        SignInStatus.Pending or SignInStatus.SlowDown => SignInStep.Waiting,
        SignInStatus.Expired => SignInStep.Expired,
        SignInStatus.SignedIn => null,
        _ => SignInStep.CouldNotFinish,
    };

    /// <summary>Why it failed, as KIFF's status only: never its body.</summary>
    public static string Why(WordBook w, SignInResult result) =>
        w.Get("error.kiff_answered", ("status", Status(result.Detail)));

    /// <summary>Why a start failed: KIFF's status and error code (<see cref="DeviceSignIn.StartRefusal"/>).</summary>
    public static string WhyStart(WordBook w, string refusal) => w.Get("error.kiff_answered", ("status", refusal));

    static string Status(string? detail)
    {
        var digits = new string((detail ?? "").TakeWhile(char.IsAsciiDigit).ToArray());
        return digits.Length > 0 ? digits : "?";
    }

    static SignInCode Code(SignInCode? code) => code ?? throw new ArgumentNullException(nameof(code));
}
