using Tafs.Core.Words;

namespace Tafs.Core.Coach;

/// <summary>
/// A link in an answer from a page turn (<see cref="ChatMessage.PageTurn"/>), or in an answer still being written, which can't
/// be told apart yet: it asks before it opens, as the phones' <c>PageLinkGuard</c>. The page Coach read could have put the link
/// there, so the person sees where it really goes, the whole address and never the link's words, and only an <c>https</c>
/// address can be opened. Any other one gets its address and Cancel alone.
/// </summary>
public static class PageLinks
{
    /// <summary>
    /// Opened from the confirm: <c>https</c> with a host only. Plain <c>http</c> goes unencrypted, so the network could change
    /// where it lands; another scheme (<c>tel:</c>, <c>javascript:</c>, an app's) opens something other than a page.
    /// </summary>
    public static bool Opens(Uri address) => address.IsAbsoluteUri && address.Scheme == Uri.UriSchemeHttps && address.Host.Length > 0;

    /// <summary>
    /// The address as the confirm shows it: whole, and isolated left to right (U+2066 … U+2069), so in Arabic, Hebrew or Urdu
    /// the sentence around it can't reorder its parts into an address that isn't the one that opens. It's the escaped form,
    /// the one that's launched: a direction mark or override, a space or a control inside it shows as its <c>%</c> code, so
    /// nothing in the address can hide where it goes.
    /// </summary>
    public static string Shown(Uri address) => "\u2066" + address.AbsoluteUri + "\u2069";

    /// <summary>"Open this link?" (<c>chat.link_confirm_title</c>).</summary>
    public static string Title(WordBook w) => w.Get("chat.link_confirm_title");

    /// <summary>"It came from a page Coach read. It goes to: {address}" (<c>chat.link_confirm_body</c>), with the address <see cref="Shown"/>.</summary>
    public static string Body(WordBook w, Uri address) => w.Get("chat.link_confirm_body", ("address", Shown(address)));

    /// <summary>"Open" (<c>chat.link_confirm_open</c>), only for an address that <see cref="Opens"/>; else none, and Cancel alone.</summary>
    public static string? Open(WordBook w, Uri address) => Opens(address) ? w.Get("chat.link_confirm_open") : null;
}
