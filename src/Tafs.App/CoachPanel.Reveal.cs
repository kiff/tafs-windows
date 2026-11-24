using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Coach;
using Tafs.Core.Words;

namespace Tafs.App;

/// <summary>
/// Coach's answer revealed as it arrives while the panel is open, as the iPad's <c>RevealingBubble</c> (kiff/tafs#58): tafs
/// cloud hands each message over whole, and the reveal shows it being read in, not written: about 400 characters a second,
/// two seconds at most for a long answer, with no Markdown symbol showing on the way (<see cref="Markdown.Healed"/>). Older
/// messages, and every message when Windows' animations are off, show at once.
/// </summary>
public sealed partial class CoachPanel
{
    /// <summary>Messages already shown in this conversation: none of them reveals again.</summary>
    readonly HashSet<string> seenMessages = [];
    /// <summary>The conversation they were seen in: a new one marks its messages seen as it first draws.</summary>
    string? seenIn;
    /// <summary>Answers being revealed: how far, and the host that draws them now (a redraw hands over a new one).</summary>
    readonly Dictionary<string, (int Shown, string Text, Border Host, bool Ask)> revealing = [];
    DispatcherTimer? revealClock;

    static string Key(ChatMessage m) => m.ServerId ?? $"{m.Session}|{m.At.ToUnixTimeMilliseconds()}|{m.Text.GetHashCode()}";

    static bool Animates => new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

    /// <summary>Coach's words, drawn whole, or revealed when this answer arrived while the panel was open.</summary>
    UIElement Revealing(ChatMessage m)
    {
        var key = Key(m);
        if (seenIn != m.Session)
        {
            // A conversation first drawn: what's in it was there before, and shows at once.
            seenIn = m.Session;
            seenMessages.Clear();
            revealing.Clear();
            foreach (var other in thread?.Messages ?? []) seenMessages.Add(Key(other));
        }
        if (revealing.TryGetValue(key, out var going))
        {
            var again = new Border { Child = MarkdownView.Build(Markdown.Healed(going.Text[..going.Shown]), 16, Brush, going.Ask) };
            revealing[key] = going with { Host = again };
            return again;
        }
        if (!seenMessages.Add(key) && !revealing.ContainsKey(key) || !Animates || m.Text.Length < 2)
            return MarkdownView.Build(m.Text, 16, Brush, m.PageTurn);
        var host = new Border { Child = MarkdownView.Build("", 16, Brush, m.PageTurn) };
        revealing[key] = (0, m.Text, host, m.PageTurn);
        if (revealClock is null)
        {
            revealClock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            revealClock.Tick += (_, _) => Reveal();
        }
        revealClock.Start();
        return host;
    }

    void Reveal()
    {
        foreach (var key in revealing.Keys.ToList())
        {
            var (shown, text, host, ask) = revealing[key];
            // About 400 characters a second, two seconds at most for a long answer.
            var step = Math.Max(8, text.Length / 120);
            shown = Math.Min(text.Length, shown + step);
            if (shown >= text.Length)
            {
                host.Child = MarkdownView.Build(text, 16, Brush, ask);
                revealing.Remove(key);
                continue;
            }
            host.Child = MarkdownView.Build(Markdown.Healed(text[..shown]), 16, Brush, ask);
            revealing[key] = (shown, text, host, ask);
        }
        if (revealing.Count == 0) revealClock?.Stop();
    }
}
