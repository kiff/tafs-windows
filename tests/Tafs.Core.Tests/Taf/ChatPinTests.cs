using Tafs.Core.Taf;

namespace Tafs.Core.Tests.Taf;

/// <summary>The sent message at the top of a chat, as the iPad's PinnedTurn.</summary>
[Part("record")]
public class ChatPinTests
{
    [Fact]
    public void A_short_turn_gets_the_room_to_reach_the_top_and_the_room_shrinks_as_the_answer_grows()
    {
        // A 600 high view, a 60 high bubble just sent, the scroll's 24 under the thread.
        Assert.Equal(508, ChatPin.Room(600, 60, 24));
        // Exactly enough: the bubble's top, less 8, plus the view, is the thread's foot with its room and padding.
        const double top = 1000;
        Assert.Equal(ChatPin.Offset(top) + 600, top + 60 + ChatPin.Room(600, 60, 24) + 24);
        Assert.Equal(208, ChatPin.Room(600, 360, 24));
        // The answer has filled the screen: no room, and the bubble stays where it is.
        Assert.Equal(0, ChatPin.Room(600, 900, 24));
        Assert.Equal(0, ChatPin.Offset(4));
    }
}
