namespace Tafs.Core.Taf;

/// <summary>
/// The sent message at the top of a chat, as the iPad's <c>PinnedTurn</c>: the room under its turn, and where the view goes.
/// </summary>
public static class ChatPin
{
    /// <summary>Space over the pinned bubble when it's at the top.</summary>
    public const double Over = 8;

    /// <summary>
    /// The room under the pinned turn (from its bubble's top to the thread's foot), so the bubble reaches the top at once and
    /// the answer grows into the room: the screen less the turn, less the space over the bubble and the scroll's own padding
    /// under the thread. None once the turn fills the screen.
    /// </summary>
    public static double Room(double viewport, double turn, double paddingUnder) => Math.Max(0, viewport - turn - Over - paddingUnder);

    /// <summary>Where the view goes: the bubble's top, less the space over it.</summary>
    public static double Offset(double bubbleTop) => Math.Max(0, bubbleTop - Over);
}
