using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Tafs.App;

/// <summary>
/// Buttons in rows that wrap, as the iPad's <c>WrapRow</c> (<c>FlowRow</c>): each at its own width, never wider than the
/// row, so a long one wraps its lines inside it; a button that doesn't fit starts the next row.
/// </summary>
sealed class WrapRow : Panel
{
    public double Spacing { get; set; } = 8;

    protected override Size MeasureOverride(Size available)
    {
        var width = available.Width;
        double x = 0, y = 0, row = 0, widest = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > width)
            {
                x = 0;
                y += row + Spacing;
                row = 0;
            }
            x += size.Width + Spacing;
            widest = Math.Max(widest, x - Spacing);
            row = Math.Max(row, size.Height);
        }
        return new Size(double.IsInfinity(width) ? widest : width, y + row);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double x = 0, y = 0, row = 0;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > final.Width)
            {
                x = 0;
                y += row + Spacing;
                row = 0;
            }
            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width + Spacing;
            row = Math.Max(row, size.Height);
        }
        return final;
    }
}
