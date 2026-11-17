namespace Tafs.App;

/// <summary>The sent message at the top of a taf's chat (<see cref="ChatPinner"/>), as the iPad's TafChatView.</summary>
public sealed partial class TafPage
{
    ChatPinner? pinner;

    /// <summary>The page's pin: its scroll, the thread's panel, the scroll's 24 under it, and only while Chat shows.</summary>
    ChatPinner Pinner => pinner ??= new ChatPinner(PageScroll, ChatPane, 24, () => tab == Tafs.Core.Taf.TafTab.Chat);

    static T Own<T>(T bubble, string text) where T : Microsoft.UI.Xaml.FrameworkElement => ChatPinner.Own(bubble, text);

    void PinNext(string text) => Pinner.Next(text);

    void Unpin() => Pinner.Clear();

    bool PinSent() => Pinner.Pin();
}
