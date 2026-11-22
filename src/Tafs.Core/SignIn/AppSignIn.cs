using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tafs.Core.Kiff;

namespace Tafs.Core.SignIn;

/// <summary>
/// Signing in inside the app (K5, kiff/cloud <c>api/internal/appsignin/README.md</c>; the iPad's and Android's
/// <c>AppSignIn</c>, tafs-linux's <c>app_signin</c>): KIFF's <c>/oauth/authorize</c> with PKCE in the person's browser, the
/// way back at <c>dev.kiff.tafs:/oauth</c>, then <c>POST /v1/auth/app/token</c> with the terms version the welcome's tick
/// agreed to, for the device flow's own session. On Windows the browser is the person's own, with its cookies; KIFF
/// never issues a code without the person acting on its page for this request (kiff/cloud#23). Transport is the
/// caller's; nothing here touches the network.
/// </summary>
public static class AppSignIn
{
    /// <summary>The one client every tafs app is (first party: no consent page).</summary>
    public const string ClientId = "tafs-app";
    /// <summary>The scheme KIFF sends the code back on: a private-use scheme (RFC 8252 §7.1), which PKCE makes safe.</summary>
    public const string Scheme = "dev.kiff.tafs";
    /// <summary>The one way back KIFF accepts, exactly.</summary>
    public const string RedirectUri = Scheme + ":/oauth";
    public const string TokenPath = "/v1/auth/app/token";
    public const string TermsPath = "/v1/auth/app/terms";
    /// <summary>How long a code KIFF refused only for changed terms is sent again: 50 of its 60 seconds.</summary>
    public static readonly TimeSpan KeptCode = TimeSpan.FromSeconds(50);
    /// <summary>The languages KIFF's sign-in page speaks (<c>ui_locales</c>); any other is left out.</summary>
    public static readonly IReadOnlySet<string> PageLanguages = new HashSet<string> { "en", "de", "fr", "es", "it", "pt", "nl", "pl" };

    /// <summary>KIFF's authorize page.</summary>
    public static readonly Uri Authorize = new(DeviceSignIn.Api, "/oauth/authorize");

    /// <summary>A code's way back and the terms it went with, for a code KIFF refused only because the terms changed.</summary>
    public sealed record Kept(Attempt Attempt, string Code, DateTimeOffset At)
    {
        public override string ToString() => "Kept(…)";
    }

    /// <summary>One attempt: its PKCE verifier and its <c>state</c>, made fresh for each Continue. Neither is ever printed.</summary>
    public sealed class Attempt
    {
        public string Verifier { get; }
        public string State { get; }

        Attempt(string verifier, string state) => (Verifier, State) = (verifier, state);

        /// <summary>A fresh verifier (43 characters, RFC 7636's least) and a fresh <c>state</c> (256 bits).</summary>
        public static Attempt New() => new(Random43(), Random43());

        /// <summary>For tests: a known verifier and state.</summary>
        public static Attempt With(string verifier, string state) => new(verifier, state);

        static string Random43() => Base64Url(RandomNumberGenerator.GetBytes(32));

        /// <summary>S256: base64url, no padding, of the verifier's SHA-256. <c>plain</c> is never used.</summary>
        public string Challenge => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)));

        public override string ToString() => "Attempt(…)";

        /// <summary>KIFF's authorize link for this attempt, in the app's language where KIFF's page speaks it.</summary>
        public Uri AuthorizeLink(string language, string? loginHint = null)
        {
            var q = new List<(string, string)>
            {
                ("response_type", "code"), ("client_id", ClientId), ("redirect_uri", RedirectUri),
                ("code_challenge", Challenge), ("code_challenge_method", "S256"), ("state", State),
            };
            var code = KiffCode(language);
            if (PageLanguages.Contains(code)) q.Add(("ui_locales", code));
            if (!string.IsNullOrEmpty(loginHint)) q.Add(("login_hint", loginHint));
            var query = string.Join("&", q.Select(p => Uri.EscapeDataString(p.Item1) + "=" + Uri.EscapeDataString(p.Item2)));
            return new Uri(Authorize + "?" + query);
        }

        /// <summary>
        /// What a link back says, for this attempt only (the iPad's <c>AppSignIn.answer</c>). It's ours only when it is
        /// <c>dev.kiff.tafs:/oauth</c> exactly, with no host and no fragment, and carries this attempt's <c>state</c>, each
        /// field once; anything else is null and never used, not even as a refusal.
        /// </summary>
        public Back? Answer(string link)
        {
            if (!link.StartsWith(Scheme + ":", StringComparison.Ordinal)) return null;
            var rest = link[(Scheme.Length + 1)..];
            if (rest.Contains('#')) return null;
            var q = rest.IndexOf('?');
            var path = q < 0 ? rest : rest[..q];
            if (path != "/oauth") return null;
            string? state = null, code = null, error = null;
            foreach (var pair in (q < 0 ? "" : rest[(q + 1)..]).Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                var name = Unescape(eq < 0 ? pair : pair[..eq]);
                var value = Unescape(eq < 0 ? "" : pair[(eq + 1)..]);
                if (name is null || value is null) return null;
                ref string? slot = ref state;
                switch (name)
                {
                    case "state": slot = ref state; break;
                    case "code": slot = ref code; break;
                    case "error": slot = ref error; break;
                    default: continue;
                }
                // A field given twice is never ours: which one counts would be a guess.
                if (slot is not null) return null;
                slot = value;
            }
            if (state != State) return null;
            return (code, error) switch
            {
                ({ Length: > 0 }, null) => new Back.Code(code),
                (null, { } e) => new Back.Refused(e),
                _ => null,
            };
        }

        /// <summary>
        /// <c>POST /v1/auth/app/token</c>'s body: the code, this attempt's verifier, the terms version the tick agreed to,
        /// and the device exactly as <c>/v1/auth/device/start</c> sends it (<see cref="DeviceSignIn.StartBody"/>). The
        /// OAuth fields are written last, so no device field can stand in for one.
        /// </summary>
        public byte[] TokenBody(string code, string termsVersion, byte[] deviceStartBody)
        {
            var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(deviceStartBody) ?? [];
            var output = body.ToDictionary(p => p.Key, p => (object)p.Value);
            output["client_id"] = ClientId;
            output["code"] = code;
            output["code_verifier"] = Verifier;
            output["redirect_uri"] = RedirectUri;
            output["terms_version"] = termsVersion;
            return JsonSerializer.SerializeToUtf8Bytes(output);
        }
    }

    /// <summary>What came back from KIFF's page for this attempt.</summary>
    public abstract record Back
    {
        /// <summary>A code, good for 60 seconds, once. Never printed.</summary>
        public sealed record Code(string Value) : Back { public override string ToString() => "Code(…)"; }
        /// <summary>KIFF's <c>error</c>: <c>access_denied</c>, <c>unsupported_client</c>, <c>temporarily_unavailable</c>…</summary>
        public sealed record Refused(string Error) : Back;
    }

    /// <summary>How Continue ended when it didn't sign in (the iPad's <c>continueOutcome</c>).</summary>
    public enum Outcome
    {
        /// <summary>The person declined on KIFF's page: nothing is said, the welcome is as it was.</summary>
        Declined,
        /// <summary>Signing in inside the app is off at KIFF: its words, or "isn't on yet".</summary>
        NotOn,
        /// <summary>The terms changed since the tick: the box is unticked, and the welcome says why.</summary>
        TermsChanged,
        /// <summary>Anything else: signing in didn't work, the tick kept, Continue free again.</summary>
        Failed,
    }

    /// <summary>What KIFF's refusal on the way back means.</summary>
    public static Outcome BackRefused(string error) => error switch
    {
        "access_denied" => Outcome.Declined,
        "unsupported_client" => Outcome.NotOn,
        _ => Outcome.Failed,
    };

    /// <summary>The token's answer: the session (the device flow's own), or how Continue ends, with KIFF's own words when it sent some.</summary>
    public static (SignedInSession? Session, Outcome Outcome, string? Words) ReadToken(int status, string body)
    {
        if (status == 200)
        {
            var read = DeviceSignIn.ReadToken(status, body);
            return read.Session is { } s ? (s, Outcome.Failed, null) : (null, Outcome.Failed, null);
        }
        string? error = null, words = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) error = e.GetString();
                if (doc.RootElement.TryGetProperty("words", out var w) && w.ValueKind == JsonValueKind.String) words = w.GetString();
            }
        }
        catch (JsonException) { }
        return (status, error) switch
        {
            (409, "terms_outdated") => (null, Outcome.TermsChanged, null),
            (_, "unsupported_client") => (null, Outcome.NotOn, string.IsNullOrWhiteSpace(words) ? null : words),
            _ => (null, Outcome.Failed, null),
        };
    }

    /// <summary><c>GET /v1/auth/app/terms</c>, read: the terms version in force; null when it can't be read.</summary>
    public static string? ReadTerms(int status, string body)
    {
        if (status is < 200 or >= 300) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("terms_version", out var t) && t.ValueKind == JsonValueKind.String
                && t.GetString() is { Length: > 0 } v ? v : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>A code KIFF refused only because the terms changed, still good to send with the new tick.</summary>
    public static bool StillGood(Kept? kept, DateTimeOffset now) => kept is not null && now - kept.At < KeptCode;

    /// <summary>KIFF knows a language by its code: pt-BR is "pt", zh-Hant "zh", sr-Latn "sr".</summary>
    public static string KiffCode(string language) => language switch
    {
        "pt-BR" => "pt",
        "zh-Hant" => "zh",
        "sr-Latn" => "sr",
        _ => language,
    };

    static string? Unescape(string s)
    {
        try { return Uri.UnescapeDataString(s.Replace('+', ' ')); }
        catch (UriFormatException) { return null; }
    }

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
