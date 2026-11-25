using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Home;
using Windows.Foundation;

namespace Tafs.App;

/// <summary>
/// A page's column (<see cref="PageWidths"/>): capped at its kind's width and centred in the page, the same rule on every app.
/// It asks for no more than the page's own width, so a child that wants to be wider is laid out inside the column and never
/// pushes the column past the window's edge.
/// </summary>
public sealed partial class PageColumn : Panel
{
    /// <summary>The page's kind by name, as XAML gives it (<c>Reading</c>, <c>List</c>, <c>Gallery</c>, <c>Brief</c>, <c>Calendar</c>).</summary>
    public string Kind { get; set; } = nameof(PageKind.Reading);

    PageKind Is => Enum.TryParse<PageKind>(Kind, out var kind) ? kind : PageKind.Reading;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? PageWidths.Cap(Is) : PageWidths.Place(availableSize.Width, Is).Width;
        double height = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(width, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (left, width) = PageWidths.Place(finalSize.Width, Is);
        foreach (var child in Children) child.Arrange(new Rect(left, 0, width, finalSize.Height));
        return finalSize;
    }
}
