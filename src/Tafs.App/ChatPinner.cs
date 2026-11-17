using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// The sent message at the top, as the iPad's TafChatView and SetupChatView: once the person sends,
/// their message moves to the top of the chat and stays there while the answer grows below it, rather than the answer
/// pushing it away. Under it, room enough for it to reach the top at once: the screen less its turn (the iPad's
/// <c>PinnedTurn.room</c>), which shrinks as the answer grows into it. Each redraw puts it back at the top, unless the person
/// has scrolled since; the next message sent is pinned in its place. Used by a taf's chat and by Coach.
/// </summary>
sealed class ChatPinner
{
    readonly ScrollViewer scroll;
    readonly Panel thread;
    readonly double paddingUnder;
    readonly Func<bool> showing;

    /// <summary>What the person just sent, pinned at the top once its bubble is drawn; none when nothing is pinned.</summary>
    string? words;

    /// <summary>The first move to the top slides; putting it back after a redraw doesn't.</summary>
    bool animate;

    /// <summary>The person scrolled since it was pinned: it's left where they put it, until the next message.</summary>
    bool touched;

    /// <summary>Where the pin last put the view, so a move of the person's own reads as theirs.</summary>
    double offset = -1;

    /// <summary>The room under the pinned turn, at the thread's end.</summary>
    readonly Border room = new() { Height = 0 };

    /// <param name="scroll">The chat's scroll.</param>
    /// <param name="thread">The panel the thread is drawn in, inside it.</param>
    /// <param name="paddingUnder">The scroll's own space under the thread.</param>
    /// <param name="showing">Whether the chat shows now: the pin moves nothing otherwise.</param>
    public ChatPinner(ScrollViewer scroll, Panel thread, double paddingUnder, Func<bool> showing)
    {
        (this.scroll, this.thread, this.paddingUnder, this.showing) = (scroll, thread, paddingUnder, showing);
        // A move of the view that isn't the pin's own is the person's: the pin stays where they put it.
        scroll.ViewChanged += (_, e) =>
        {
            if (!e.IsIntermediate && words is not null && offset >= 0 && Math.Abs(scroll.VerticalOffset - offset) > 2) touched = true;
        };
    }

    /// <summary>A bubble of the person's own words, which the pin finds by its words.</summary>
    sealed record OwnWords(string Text);

    /// <summary>Marks a bubble of the person's own words.</summary>
    public static T Own<T>(T bubble, string text) where T : FrameworkElement
    {
        if (text.Trim().Length > 0) bubble.Tag = new OwnWords(text.Trim());
        return bubble;
    }

    /// <summary>Pins what's being sent: the next redraw puts its bubble at the top.</summary>
    public void Next(string text)
    {
        words = text.Trim().Length > 0 ? text.Trim() : null;
        (animate, touched, offset) = (true, false, -1);
    }

    /// <summary>Nothing pinned: another chat, or a message that wasn't sent at all.</summary>
    public void Clear() => (words, touched, offset) = (null, false, -1);

    /// <summary>
    /// After the thread is drawn: the pinned message's bubble (the last with its words) at the top, with the room under its
    /// turn. False when nothing is pinned, or its bubble isn't drawn any more, which ends the pin: the caller then follows the
    /// thread's end as before.
    /// </summary>
    public bool Pin()
    {
        if (words is not { } pinned) return false;
        var bubble = thread.Children.OfType<FrameworkElement>().LastOrDefault(e => e.Tag is OwnWords own && own.Text == pinned);
        if (bubble is null)
        {
            Clear();
            return false;
        }
        room.Height = 0;
        if (room.Parent is Panel old) old.Children.Remove(room);
        thread.Children.Add(room);
        thread.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!showing() || !ReferenceEquals(bubble.Parent, thread) || scroll.Content is not UIElement content) return;
            scroll.UpdateLayout();
            double Y(UIElement e) => e.TransformToVisual(content).TransformPoint(default).Y;
            var top = Y(bubble);
            room.Height = ChatPin.Room(scroll.ViewportHeight, Y(room) - top, paddingUnder);
            if (touched) return;
            scroll.UpdateLayout();
            offset = ChatPin.Offset(top);
            scroll.ChangeView(null, offset, null, disableAnimation: !animate);
            animate = false;
        });
        return true;
    }
}
