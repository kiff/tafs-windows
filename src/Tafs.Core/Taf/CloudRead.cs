using System.Globalization;
using System.Text;
using System.Text.Json;
using Tafs.Core.Account;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>
/// A read of tafs cloud: its answer, or why it didn't come in the person's words, as the iPad's
/// <c>CloudError.errorDescription</c> (TafsCloud.swift), with the status it answered (0 when it wasn't reached), and
/// tafs cloud's <c>code</c> for a refusal when it gives one (<c>ai_consent_required</c>), and its <c>words</c> alone, written
/// for the person (the iPad's <c>CloudError.words</c>), for a screen that says only those.
/// </summary>
public sealed record CloudRead(JsonElement? Answer, string? Why = null, int Status = 0, string? Code = null, string? Words = null)
{
    /// <summary>
    /// A refusal because of a halt, as tafs cloud says it (<c>halted</c>, the iPad's <c>CloudError.halted</c>): the whole
    /// <c>account</c>, or the <c>taf</c> on its own; none for any other refusal, or a word it doesn't know.
    /// </summary>
    public string? Halted { get; init; }

    /// <summary>
    /// The AI-data consent's screen, when the refusal carries one the app can draw (the iPad's <c>CloudError.consent</c>): a
    /// turn refused for want of it, or an older screen's yes refused with the new words (409).
    /// </summary>
    public AiConsent? Consent { get; init; }

    /// <summary>For 409 <c>cards_in_force</c>: the Cards KIFF still holds in force, by id (the iPad's <c>CloudError.cards</c>).</summary>
    public IReadOnlyList<string> Cards { get; init; } = [];

    /// <summary>tafs cloud's code for a model turn refused for want of the AI-data consent.</summary>
    public const string AiConsentRequired = "ai_consent_required";

    /// <summary>A model turn refused for want of the AI-data consent, with its screen to put up.</summary>
    public bool AsksConsent => Code == AiConsentRequired && Consent is not null;

    /// <summary>
    /// A turn refused for want of the consent, from any route, as the iPad's <c>.aiConsentRequired</c>: said once here, so
    /// its screen shows wherever the person tried, with nothing wired per call.
    /// </summary>
    public static event Action<AiConsent>? ConsentRequired;

    public static CloudRead Of(JsonElement answer) => new(answer, Status: 200);

    /// <summary>
    /// tafs cloud's code for a message sent under an id already used for other words (kiff/tafs docs/api.md › A taf's chat):
    /// it isn't sent; the person sends it again as a new message, under a new id.
    /// </summary>
    public const string DedupKeyConflict = "dedup_key_conflict";

    /// <summary>
    /// A refusal, as the iPad's <c>TafsCloud.refusal</c>: tafs cloud's <c>words</c>, written for the person, else its
    /// <c>error</c>, else "tafs cloud answered 503".
    /// </summary>
    public static CloudRead Refused(WordBook w, int status, string body)
    {
        JsonElement json = default;
        try { json = JsonDocument.Parse(body).RootElement.Clone(); }
        catch (JsonException) { }
        // Another message under an id already used (409 dedup_key_conflict): the app's own words, as every app says it.
        var conflict = status == 409 && Lenient.Text(json, "code") == DedupKeyConflict;
        var read = new CloudRead(null, conflict ? w.Get("error.sent_with_other_text") : Lenient.Text(json, "words") ?? Lenient.Text(json, "error")
            ?? w.Get("error.cloud_answered", ("status", status.ToString(CultureInfo.InvariantCulture))), status, Lenient.Text(json, "code"),
            Lenient.Text(json, "words"))
        {
            Halted = Lenient.Text(json, "halted") is "account" or "taf" ? Lenient.Text(json, "halted") : null,
            // Read on its own: one that doesn't read leaves the rest of the refusal.
            Consent = AiConsent.Read(json) is { Drawable: true } consent ? consent : null,
            Cards = Lenient.Array(json, "cards").Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString()!).ToList(),
        };
        if (read.AsksConsent) ConsentRequired?.Invoke(read.Consent!);
        return read;
    }

    /// <summary>
    /// tafs cloud wasn't reached. The iPad shows the system's words for it; Windows has none for the person, so it's the
    /// app's own "It didn't reach the server…".
    /// </summary>
    public static CloudRead Unreached(WordBook w) => new(null, w.Get("error.offline"));

    /// <summary>A part of a path, as the iPad's <c>TafsCloud.escaped</c>: what a path allows, less "/".</summary>
    public static string PathPart(string s) => Escape(s, "-._~!$&'()*+,;=:@");

    /// <summary>A query's value, as the iPad's <c>query</c>: letters and digits as they are, every other character escaped.</summary>
    public static string QueryPart(string s) => Escape(s, "");

    /// <summary>
    /// ASCII on purpose: the iPad's character sets also keep non-ASCII letters as they are, but tafs cloud's ids, filters and
    /// cursors are ASCII, so nothing sent differs, and a link stays plain ASCII.
    /// </summary>
    static string Escape(string s, string keep)
    {
        var b = new StringBuilder();
        foreach (var by in Encoding.UTF8.GetBytes(s))
        {
            var c = (char)by;
            if (by < 128 && (char.IsAsciiLetterOrDigit(c) || keep.Contains(c))) b.Append(c);
            else b.Append('%').Append(by.ToString("X2", CultureInfo.InvariantCulture));
        }
        return b.ToString();
    }
}
