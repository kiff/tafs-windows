using System.Security.Cryptography;
using Tafs.Core.Words;

namespace Tafs.Core.Kiff;

/// <summary>One of KIFF's lines for a connection: the operation it's about (none for the whole connection), its kind and its text.</summary>
public sealed record ProfileLine(string? Op, string Kind, string Text)
{
    /// <summary>What KIFF can't check for this connection (missing, probation, an unpinned vendor): shown in amber.</summary>
    public bool Caution => Kind is "missing" or "probation" or "unpinned";
}

/// <summary>
/// KIFF's words for a connection's profile (<c>kiff.words.v1</c>), as the iPad's <c>ProfileWords</c>: what the person reads
/// before approving a connection, and signs. KIFF writes them, never Coach or the app: each line's text is shown verbatim, in
/// order. They're read with the session (<c>POST /v1/profiles/words</c>, the profile's RFC 8785 form as the body) and checked
/// against the profile shown before they're shown: approving then signs both digests into planque's approve body.
/// </summary>
public sealed record ProfileWords(IReadOnlyList<ProfileLine> Lines, string ProfileSha256, string WordsSha256)
{
    public const string Path = "/v1/profiles/words";
    public const string Version = "kiff.words.v1";

    /// <summary>The hex SHA-256 of a JSON value's RFC 8785 form.</summary>
    public static string Digest(JcsValue value) => Convert.ToHexString(SHA256.HashData(Jcs.Canonical(value))).ToLowerInvariant();

    /// <summary>The body KIFF renders: the profile, canonical.</summary>
    public static byte[] Body(JcsValue profile) => Jcs.Canonical(profile);

    /// <summary>
    /// KIFF's answer, checked against <paramref name="profile"/>, the profile shown: its version, that the words are for that
    /// profile, and that they're the ones their digest names. Otherwise why, in the iPad's words, and nothing to approve.
    /// </summary>
    public static (ProfileWords? Words, string? Why) Checked(WordBook w, byte[] answer, JcsValue profile)
    {
        JcsValue parsed;
        try { parsed = Jcs.Parse(answer); }
        catch (Jcs.Failure) { return NotWords(w); }
        if (parsed["words"] is not { } words || words["v"]?.AsString != Version || parsed["profile_sha256"]?.AsString is not { } profileSha
            || parsed["words_sha256"]?.AsString is not { } wordsSha || words["lines"]?.AsArray is not { } raw) return NotWords(w);
        if (Digest(profile) != profileSha) return (null, w.Get("connect.words_another_connection_than_one"));
        if (Digest(words) != wordsSha) return (null, w.Get("kiff.kiff_words_do_not_match"));
        var lines = new List<ProfileLine>();
        foreach (var line in raw)
        {
            if (line["kind"]?.AsString is not { } kind || line["text"]?.AsString is not { } text) return NotWords(w);
            lines.Add(new(line["op"]?.AsString, kind, text));
        }
        return (new(lines, profileSha, wordsSha), null);
    }

    static (ProfileWords?, string?) NotWords(WordBook w) => (null, w.Get("connect.kiff_words_connection_could_not"));

    /// <summary>Whether these words are for exactly <paramref name="profile"/>: the only words shown for a grant.</summary>
    public bool For(JcsValue profile) => Digest(profile) == ProfileSha256;
}
