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

    /// <summary>Why a start failed: KIFF's status, or "?" for an answer that couldn't be used.</summary>
    public static string WhyStart(WordBook w, int status) =>
        w.Get("error.kiff_answered", ("status", status > 0 ? status.ToString(System.Globalization.CultureInfo.InvariantCulture) : "?"));

    static string Status(string? detail)
    {
        var digits = new string((detail ?? "").TakeWhile(char.IsAsciiDigit).ToArray());
        return digits.Length > 0 ? digits : "?";
    }

    static SignInCode Code(SignInCode? code) => code ?? throw new ArgumentNullException(nameof(code));
}
