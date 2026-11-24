using Tafs.Core.Words;

namespace Tafs.Core.Coach;

/// <summary>Where a chat link is: in an answer, in an answer from a page turn, or in an answer still being written.</summary>
public enum LinkFrom { Answer, PageTurn, StillWriting }

/// <summary>What a click on a chat link does: open it, ask first, or say it won't open.</summary>
public enum LinkStep { Open, Ask, Blocked }

/// <summary>
/// A link in a chat's answer, as the phones' <c>PageLinkGuard</c>. Only a clean <c>https</c> address ever opens (<see cref="Opens"/>):
/// any other one, in any answer, says "This link won't open", with its address and Cancel alone. A clean one opens at once,
/// except in an answer from a page turn (<see cref="ChatMessage.PageTurn"/>), or one still being written, which can't be told
/// apart yet: the page Coach read could have put the link there, so it asks first, showing where it really goes, the whole
/// address and never the link's words.
/// </summary>
public static class PageLinks
{
    /// <summary>What a click does: never open what <see cref="Opens"/> refuses; ask first from a page turn or an answer still being written.</summary>
    public static LinkStep Step(Uri address, LinkFrom from) =>
        !Opens(address) ? LinkStep.Blocked : from == LinkFrom.Answer ? LinkStep.Open : LinkStep.Ask;

    /// <summary>
    /// Opened from the confirm: <c>https</c> with a host only, and nothing in the address, as written or percent-decoded, that's a
    /// space, a control or a format character (a direction mark or override, an isolate), as the phones' rule. Plain <c>http</c>
    /// goes unencrypted, so the network could change where it lands; another scheme (<c>tel:</c>, <c>javascript:</c>, an app's)
    /// opens something other than a page; a hidden character makes an address that reads as another one.
    /// </summary>
    public static bool Opens(Uri address) =>
        address.IsAbsoluteUri && address.Scheme == Uri.UriSchemeHttps && address.Host.Length > 0
        && Clean(address.OriginalString) && Clean(Uri.UnescapeDataString(address.AbsoluteUri));

    static bool Clean(string text) => !text.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)
        || System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format);

    /// <summary>
    /// The address as the confirm shows it: whole, and isolated left to right (U+2066 … U+2069), so in Arabic, Hebrew or Urdu
    /// the sentence around it can't reorder its parts into an address that isn't the one that opens. It's the escaped form,
    /// the one that's launched: a direction mark or override, a space or a control inside it shows as its <c>%</c> code, so
    /// nothing in the address can hide where it goes.
    /// </summary>
    public static string Shown(Uri address) => "\u2066" + address.AbsoluteUri + "\u2069";

    /// <summary>"Open this link?" (<c>chat.link_confirm_title</c>).</summary>
    public static string Title(WordBook w) => w.Get("chat.link_confirm_title");

    /// <summary>
    /// "It came from a page Coach read. It goes to: {address}" (<c>chat.link_confirm_body</c>); in an answer still being
    /// written, "This answer is still being written. This link goes to: {address}" (<c>chat.link_confirm_body_live</c>). The
    /// address is <see cref="Shown"/>.
    /// </summary>
    public static string Body(WordBook w, Uri address, LinkFrom from = LinkFrom.PageTurn) =>
        w.Get(from == LinkFrom.StillWriting ? "chat.link_confirm_body_live" : "chat.link_confirm_body", ("address", Shown(address)));

    /// <summary>"This link won't open" (<c>chat.link_blocked_title</c>).</summary>
    public static string BlockedTitle(WordBook w) => w.Get("chat.link_blocked_title");

    /// <summary>"tafs opens only secure web pages from a chat. This link goes to: {address}" (<c>chat.link_blocked_body</c>), the address <see cref="Shown"/>.</summary>
    public static string BlockedBody(WordBook w, Uri address) => w.Get("chat.link_blocked_body", ("address", Shown(address)));

    /// <summary>"Open" (<c>chat.link_confirm_open</c>), only for an address that <see cref="Opens"/>; else none, and Cancel alone.</summary>
    public static string? Open(WordBook w, Uri address) => Opens(address) ? w.Get("chat.link_confirm_open") : null;
}
