using Tafs.Core.Words;

namespace Tafs.Core.Account;

/// <summary>
/// Account's explanations, each beside the action it explains, the same on every app: Chrome's line under the Chrome row, only
/// where planque offers Chromes (as Account shows Chrome only then); the emailed code's warning in the sign-in section, only where
/// KIFF can send one (its setting's <c>available</c>). So the app never explains what the person can't use, and never in a
/// section of its own with nothing to do there. The lines that only pointed to those rows ("Account › Chrome…", "Turn it on in
/// Account › Sign-in…") aren't shown: the row is right there.
/// </summary>
public static class HelpSections
{
    /// <summary>What a Chrome does for tafs, under Account's Chrome row.</summary>
    public static string? ChromeLine(WordBook w, bool chromeOffered) =>
        chromeOffered ? w.Get("help.chrome_lead", ("needs_you", w.Get("needs_you.title"))) : null;

    /// <summary>Never give the code away, in Account's sign-in section, under the setting.</summary>
    public static string? CodeLine(WordBook w, bool codeAvailable) => codeAvailable ? w.Get("help.code_safe") : null;
}
