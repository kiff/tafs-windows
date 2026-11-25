namespace Tafs.Core.Home;

/// <summary>What a page shows, which sets how wide its column may grow.</summary>
public enum PageKind { Reading, List, Gallery, Brief, Calendar }

/// <summary>
/// Every page's column, the same on every app: capped at its kind's width, the iPad's and the Mac's caps, and centred in the
/// window. 720 for reading pages (Account, Devices, People, Alerts, Needs you, Connect apps, Plan &amp; usage, Block all),
/// 980 for list pages (Overview, an app's connections), 640 for the gallery, 1100 for the Brief, 1180 for Calendar. A window
/// narrower than the cap gives the column all of its width, so nothing changes there.
/// </summary>
public static class PageWidths
{
    public static double Cap(PageKind kind) => kind switch
    {
        PageKind.List => 980,
        PageKind.Gallery => 640,
        PageKind.Brief => 1100,
        PageKind.Calendar => 1180,
        _ => 720,
    };

    /// <summary>The column's left edge and width in a page <paramref name="available"/> wide: centred, never past the page.</summary>
    public static (double Left, double Width) Place(double available, PageKind kind)
    {
        if (double.IsNaN(available) || available <= 0) return (0, 0);
        var width = Math.Min(available, Cap(kind));
        return ((available - width) / 2, width);
    }
}
