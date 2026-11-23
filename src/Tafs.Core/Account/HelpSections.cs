using Tafs.Core.Words;

namespace Tafs.Core.Account;

/// <summary>One section of How tafs works beyond the three cards: its title, its lead line and a quieter line under it.</summary>
public sealed record HelpSection(string Id, string Title, string Lead, string More);

/// <summary>
/// How tafs works' sections after the three cards, as the iPad's <c>HowTafsWorksView</c>, each only where the person can use
/// it, so the app never explains what they can't: Chrome, only where planque offers it (as Account shows Chrome only then);
/// signing in with an emailed code, only where KIFF can send one (its setting's <c>available</c>).
/// </summary>
public static class HelpSections
{
    public static IReadOnlyList<HelpSection> Shown(WordBook w, bool chromeOffered, bool codeAvailable)
    {
        var shown = new List<HelpSection>();
        if (chromeOffered)
            shown.Add(new("chrome", w.Get("help.chrome_title"), w.Get("help.chrome_lead", ("needs_you", w.Get("needs_you.title"))),
                w.Get("help.chrome_manage", ("account", w.Get("settings.title")), ("chrome", w.Get("chrome.title")))));
        if (codeAvailable)
            shown.Add(new("code", w.Get("help.code_title"),
                w.Get("help.code_lead", ("account", w.Get("settings.title")), ("sign_in", w.Get("settings.sign_in_code.title"))), w.Get("help.code_safe")));
        return shown;
    }
}
