using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Approvals;
using Tafs.Core.Words;

namespace Tafs.App;

public sealed partial class TafPage
{
    /// <summary>
    /// The Cards a draft brings, as the owner reads them before Windows Hello (the iPad's <c>DraftCardsView</c>): its new Cards
    /// as one, "A new Card"; each Card it revises as it is now and as it will be ("Changes a Card", Now, After); one re-issued as
    /// it is, "Unchanged", its terms once; and each Card it removes, its lines struck through, with "Your taf can't do this any
    /// more.". Each in a panel of its own, in the draft's order (<see cref="AgentDraft.Shown"/>).
    /// </summary>
    static StackPanel DraftCards(AgentDraft draft, WordBook w)
    {
        var all = new StackPanel { Spacing = 14 };
        foreach (var shown in draft.Shown(w))
        {
            var inside = new List<Microsoft.UI.Xaml.UIElement>();
            switch (shown.Kind)
            {
                case DraftShownKind.Unchanged:
                    // Re-issued as it is: its terms once, not a now and after.
                    inside.Add(EyebrowText(w.Get("version.unchanged")));
                    inside.AddRange(shown.Lines.Select(l => Words(l, 14, "TafsSoftText")));
                    break;
                case DraftShownKind.Changes:
                    inside.Add(EyebrowText(w.Get("version.changes_card")));
                    inside.Add(Small(w.Get("version.now")));
                    inside.AddRange(shown.Now.Select(l => Words(l, 14, "TafsSoftText")));
                    inside.Add(Small(w.Get("version.after")));
                    inside.AddRange(shown.Lines.Select(Strong));
                    break;
                default:
                    inside.Add(EyebrowText(w.Get("version.new_card")));
                    inside.AddRange(shown.Lines.Select(Strong));
                    break;
            }
            all.Children.Add(Panel([.. inside]));
        }
        foreach (var lines in draft.RetiredLines(w))
        {
            // A Card this version doesn't have: confirming removes it, so what it allows stops.
            var inside = new List<Microsoft.UI.Xaml.UIElement> { EyebrowText(w.Get("version.removes_card")) };
            foreach (var line in lines)
            {
                var struck = Words(line, 14, "TafsSoftText");
                struck.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
                inside.Add(struck);
            }
            inside.Add(Words(w.Get("version.cant_any_more"), 12, "TafsDimText"));
            all.Children.Add(Panel([.. inside]));
        }
        return all;

        static TextBlock Strong(string line)
        {
            var t = Words(line, 14, "TafsInkText");
            t.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            return t;
        }

        static TextBlock Small(string text)
        {
            var t = Words(text, 12, "TafsDimText");
            t.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            return t;
        }
    }

    static TextBlock EyebrowText(string text) => new() { Text = text, Style = Look("TafsEyebrow") };
}
