using System.Text.RegularExpressions;
using Tafs.Core.Home;

namespace Tafs.Core.Tests.Ci;

/// <summary>
/// One layout on every app: each page's content sits in a <c>PageColumn</c> of its kind, capped and centred, and no page keeps
/// a cap of its own on its root that would push it to one side or past the window's edge (Plan &amp; usage and Calendar did).
/// </summary>
[Part("ci")]
public class PageColumnsTests
{
    public static TheoryData<string, PageKind> Pages => new()
    {
        { "AccountPage", PageKind.Reading },
        { "AlertsPage", PageKind.Reading },
        { "DevicesPage", PageKind.Reading },
        { "PeoplePage", PageKind.Reading },
        { "HaltPage", PageKind.Reading },
        { "NeedsYouPage", PageKind.Reading },
        { "ConnectAppsPage", PageKind.Reading },
        { "UsagePage", PageKind.Reading },
        { "OverviewPage", PageKind.List },
        { "AppConnectionsPage", PageKind.List },
        { "GalleryPage", PageKind.Gallery },
        { "BriefPage", PageKind.Brief },
        { "CalendarPage", PageKind.Calendar },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public void Each_page_is_in_its_kind_s_centred_column(string page, PageKind kind)
    {
        var xaml = RepoFiles.Read($"src/Tafs.App/{page}.xaml");
        // The page's scroller holds the column directly.
        var column = Regex.Match(xaml, @"<ScrollViewer>\s*<local:PageColumn\b[^>]*\bKind=""(\w+)""[^>]*>\s*(?:<!--.*?-->\s*)*<(\w+)\b([^>]*)>", RegexOptions.Singleline);
        Assert.True(column.Success, $"{page}: its content isn't in a PageColumn");
        Assert.Equal(kind.ToString(), column.Groups[1].Value);
        // Its root has no cap or alignment of its own: the column has them.
        Assert.DoesNotMatch(@"\bMaxWidth=|\bHorizontalAlignment=", column.Groups[3].Value);
    }
}
