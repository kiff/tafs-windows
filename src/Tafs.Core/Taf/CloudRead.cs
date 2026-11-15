using System.Text.Json;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>
/// A read of tafs cloud: its answer, or why it didn't come in the person's words, as the iPad's
/// <c>CloudError.errorDescription</c> (TafsCloud.swift).
/// </summary>
public sealed record CloudRead(JsonElement? Answer, string? Why = null)
{
    public static CloudRead Of(JsonElement answer) => new(answer);

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
            ?? w.Get("error.cloud_answered", ("status", status.ToString(System.Globalization.CultureInfo.InvariantCulture))));
    }

    /// <summary>
    /// tafs cloud wasn't reached. The iPad shows the system's words for it; Windows has none for the person, so it's the
    /// app's own "It didn't reach the server…".
    /// </summary>
    public static CloudRead Unreached(WordBook w) => new(null, w.Get("error.offline"));
}
