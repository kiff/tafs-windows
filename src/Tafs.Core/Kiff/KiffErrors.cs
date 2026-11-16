using System.Globalization;
using Tafs.Core.Words;

namespace Tafs.Core.Kiff;

/// <summary>
/// What a person reads when a KIFF read fails, as the iPad's <c>KIFFError.errorDescription</c> (KIFFClient.swift) and
/// <c>PlainError.words(status:)</c>: a plain line of the app's own by what happened, never KIFF's reason, which is for the
/// code and the diagnostics.
/// </summary>
public static class KiffErrors
{
    /// <summary>
    /// A 401 is signed out of KIFF (<c>.unauthorized</c>); otherwise the app's line for the status: not reached (0), not
    /// allowed (403), changed (409), busy (429), down (5xx), else ours.
    /// </summary>
    public static string Words(WordBook w, int status) => w.Get(status switch
    {
        401 => "error.signed_out",
        0 => "error.offline",
        403 => "error.not_allowed",
        409 => "error.changed",
        429 => "error.busy",
        >= 500 and <= 599 => "error.down",
        _ => "error.ours",
    });

    /// <summary>The status as KIFF answered it, for the log: never shown.</summary>
    public static string Code(int status) => status.ToString(CultureInfo.InvariantCulture);
}
