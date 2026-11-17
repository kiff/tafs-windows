namespace Tafs.Core.Home;

/// <summary>
/// The window's size as it opens: near the iPad's landscape proportions (4:3), as large as sits comfortably on the screen,
/// never under the smallest size the screens are laid out for (900 by 600).
/// </summary>
public static class WindowSize
{
    /// <summary>The smallest the window goes, in view pixels.</summary>
    public const int MinWidth = 900, MinHeight = 600;

    /// <summary>The share of the screen's work area the window opens at.</summary>
    const double Share = 0.85;

    /// <summary>
    /// The size to open at and where, centred in the work area (the screen less the taskbar), all in the screen's pixels.
    /// <paramref name="scale"/> is the screen's scale (1.5 at 150%). A work area smaller than the minimum gets it all.
    /// </summary>
    public static (int X, int Y, int Width, int Height) Opening(int left, int top, int width, int height, double scale)
    {
        var (minW, minH) = Minimum(scale);
        var h = height * Share;
        var w = h * 4 / 3;
        if (w > width * Share) (w, h) = (width * Share, width * Share * 3 / 4);
        var (ow, oh) = ((int)Math.Round(Math.Min(width, Math.Max(w, minW))), (int)Math.Round(Math.Min(height, Math.Max(h, minH))));
        return (left + (width - ow) / 2, top + (height - oh) / 2, ow, oh);
    }

    /// <summary>The minimum in the screen's pixels at its scale.</summary>
    public static (int Width, int Height) Minimum(double scale) =>
        ((int)Math.Round(MinWidth * Math.Max(scale, 1)), (int)Math.Round(MinHeight * Math.Max(scale, 1)));
}
