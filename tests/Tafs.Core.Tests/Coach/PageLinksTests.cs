using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Coach;

/// <summary>
/// A link from a page turn asks before it opens, as the phones' PageLinkGuard: only a taf's answer that tafs cloud marks
/// <c>page_turn: true</c>, the whole address shown left to right, and only https opened.
/// </summary>
[Part("coach")]
public class PageLinksTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static IReadOnlyList<ChatMessage> Messages(string json) =>
        CoachThread.Read(JsonDocument.Parse(json).RootElement)!.Items.Select(i => i.Message).OfType<ChatMessage>().ToList();

    [Fact]
    public void Only_a_taf_s_answer_marked_page_turn_true_asks()
    {
        var messages = Messages("""
            {"messages": [
              {"at": "2026-11-24T08:00:00Z", "from": "owner", "text": "What's on this page?", "session": "s1", "id": "s1.in", "reading": "shop.example", "page_turn": true},
              {"at": "2026-11-24T08:00:05Z", "from": "taf", "text": "See [the offer](https://shop.example/o)", "session": "s1", "page_turn": true},
              {"at": "2026-11-24T08:01:00Z", "from": "taf", "text": "A plain answer", "session": "s1"},
              {"at": "2026-11-24T08:02:00Z", "from": "taf", "text": "Not a flag", "session": "s1", "page_turn": "true"},
              {"at": "2026-11-24T08:03:00Z", "from": "taf", "text": "Off", "session": "s1", "page_turn": false}]}
            """);
        Assert.Equal([false, true, false, false, false], messages.Select(m => m.PageTurn));
    }

    [Theory]
    [InlineData("https://shop.example/offer?id=1", true)]
    [InlineData("http://shop.example/offer", false)]
    [InlineData("mailto:someone@shop.example", false)]
    [InlineData("ftp://shop.example/file", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("tel:+3312345678", false)]
    [InlineData("file:///C:/Windows/notepad.exe", false)]
    public void Only_https_is_opened(string address, bool opens)
    {
        var uri = new Uri(address);
        Assert.Equal(opens, PageLinks.Opens(uri));
        Assert.Equal(opens ? En.Get("chat.link_confirm_open") : null, PageLinks.Open(En, uri));
    }

    [Fact]
    public void The_confirm_shows_the_whole_address_isolated_left_to_right()
    {
        var uri = new Uri("https://shop.example/a/b?c=1#d");
        Assert.Equal("⁦https://shop.example/a/b?c=1#d⁩", PageLinks.Shown(uri));
        Assert.Equal("Open this link?", PageLinks.Title(En));
        Assert.Equal("It came from a page Coach read. It goes to: ⁦https://shop.example/a/b?c=1#d⁩", PageLinks.Body(En, uri));
        // In a right-to-left language too, the address stays one isolated run.
        var ar = WordBook.Load(SharedFiles.Strings, "ar");
        Assert.Contains("⁦https://shop.example/a/b?c=1#d⁩", PageLinks.Body(ar, uri));
    }

    [Fact]
    public void Its_words_are_in_every_shipped_language()
    {
        foreach (var language in WordBook.Shipped(SharedFiles.Strings))
        {
            var w = WordBook.Load(SharedFiles.Strings, language);
            foreach (var key in new[] { "chat.link_confirm_title", "chat.link_confirm_body", "chat.link_confirm_open", "common.cancel" })
                Assert.True(w.Has(key), $"{key} in {language}");
            Assert.Contains("https://x.example/", PageLinks.Body(w, new Uri("https://x.example/")));
        }
    }

    /// <summary>What could hide where an address goes shows as its % code, in the address shown and the one opened alike.</summary>
    [Theory]
    [InlineData("https://shop.example/a‮gnp.exe", "https://shop.example/a%E2%80%AEgnp.exe")]
    [InlineData("https://shop.example/a‏b", "https://shop.example/a%E2%80%8Fb")]
    [InlineData("https://shop.example/a b", "https://shop.example/a%20b")]
    [InlineData("https://shop.example/x", "https://shop.example/%07x")]
    public void A_direction_mark_a_space_or_a_control_shows_as_its_code(string written, string shown)
    {
        var uri = new Uri(written);
        Assert.Equal("⁦" + shown + "⁩", PageLinks.Shown(uri));
        Assert.True(PageLinks.Opens(uri));
        Assert.Equal(shown, uri.AbsoluteUri);
    }
}
