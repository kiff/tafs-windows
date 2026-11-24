namespace Tafs.Core.Ci;

/// <summary>
/// <c>--size</c> for a screenshot: the window's width and height in pixels, as <c>1920x1080</c>. Without it, or with
/// anything it can't read, a screenshot is the laptop's 1280×800.
/// </summary>
public static class ScreenSize
{
    public static readonly (int Width, int Height) Laptop = (1280, 800);

    public static (int Width, int Height) Parse(string? size)
    {
        var parts = size?.Trim().ToLowerInvariant().Split('x');
        if (parts is not { Length: 2 }) return Laptop;
        if (!int.TryParse(parts[0], out var w) || !int.TryParse(parts[1], out var h)) return Laptop;
        // The window's own minimum to a 4K screen; anything outside is a typo, not a size.
        return w is >= 640 and <= 3840 && h is >= 480 and <= 2160 ? (w, h) : Laptop;
    }
}
