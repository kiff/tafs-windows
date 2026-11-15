using System.Globalization;
using System.Text;
using System.Text.Json;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>
/// A read of tafs cloud: its answer, or why it didn't come in the person's words, as the iPad's
/// <c>CloudError.errorDescription</c> (TafsCloud.swift), with the status it answered (0 when it wasn't reached).
/// </summary>
public sealed record CloudRead(JsonElement? Answer, string? Why = null, int Status = 0)
{
    public static CloudRead Of(JsonElement answer) => new(answer, Status: 200);

    /// <summary>
    /// A refusal, as the iPad's <c>TafsCloud.refusal</c>: tafs cloud's <c>words</c>, written for the person, else its
    /// <c>error</c>, else "tafs cloud answered 503".
    /// </summary>
    public static CloudRead Refused(WordBook w, int status, string body)
    {
        JsonElement json = default;
        try { json = JsonDocument.Parse(body).RootElement.Clone(); }
        catch (JsonException) { }
        return new(null, Lenient.Text(json, "words") ?? Lenient.Text(json, "error")
            ?? w.Get("error.cloud_answered", ("status", status.ToString(CultureInfo.InvariantCulture))), status);
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
