using System.Text.Json;
using Tafs.Core.Words;

namespace Tafs.Core.Account;

/// <summary>What Account › Sign-in's row offers: turning the setting off here, opening KIFF's page to turn it on, or neither.</summary>
public enum CodeSignInAction { None, TurnOff, TurnOn }

/// <summary>Account › Sign-in in words: its label and value, its help, its button (if any) and the line under it (if any).</summary>
public sealed record CodeSignInLines(string Label, string Value, string Help, CodeSignInAction Action, string? Button, string? Note);

/// <summary>
/// Account › Sign-in, the email-code setting, as the iPad's <c>CodeSignInRow</c> and KIFF's <c>GET /v1/me/sign-in</c>
/// (tafs#1711): whether KIFF may sign this person in with a code sent to their email, and whether it can at all. Off goes
/// straight from here (<c>PUT</c> with <c>email_code: false</c>). On needs a recent sign-in, which a device session never has,
/// so it opens KIFF's own sign-in page, where the person confirms it's them; the setting is read again when they come back.
/// </summary>
public sealed record CodeSignIn(bool EmailCode, bool Available)
{
    public const string Path = "/v1/me/sign-in";

    /// <summary>KIFF's own Settings › Sign-in page, where turning it on asks for a recent sign-in (the iPad's <c>dashboard/sign-in</c>).</summary>
    public static readonly Uri KiffPage = new("https://kiff.dev/dashboard/sign-in");

    /// <summary>What turns it off: <c>{"email_code": false}</c>. Turning it on is never sent from here.</summary>
    public static byte[] OffBody => "{\"email_code\":false}"u8.ToArray();

    /// <summary>
    /// KIFF's answer: <c>email_code</c> must be a boolean, else it can't be read; <c>available</c> is false unless KIFF says
    /// true, so an answer without it never offers On.
    /// </summary>
    public static CodeSignIn? Read(JsonElement a) =>
        a.ValueKind == JsonValueKind.Object && a.TryGetProperty("email_code", out var e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? new(e.GetBoolean(), a.TryGetProperty("available", out var v) && v.ValueKind == JsonValueKind.True)
            : null;

    /// <summary>
    /// The row, as the iPad's: "Sign in with a code sent by email", On or Off, its help; then Turn off while it's on; else, where
    /// KIFF can send a code, Turn on with "Turning it on opens KIFF's sign-in page…"; else "isn't available yet".
    /// </summary>
    public CodeSignInLines Lines(WordBook w)
    {
        var (label, value, help) = (w.Get("settings.sign_in_code.label"), w.Get(EmailCode ? "settings.sign_in_code.on" : "settings.sign_in_code.off"),
            w.Get("settings.sign_in_code.help"));
        if (EmailCode) return new(label, value, help, CodeSignInAction.TurnOff, w.Get("settings.sign_in_code.turn_off"), null);
        if (Available) return new(label, value, help, CodeSignInAction.TurnOn, w.Get("settings.sign_in_code.turn_on"), w.Get("settings.sign_in_code.turn_on_in_browser"));
        return new(label, value, help, CodeSignInAction.None, null, w.Get("settings.sign_in_code.not_available"));
    }
}
