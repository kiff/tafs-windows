using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Tafs.App;

/// <summary>
/// The owner's approved first-time and empty-state drawings, one per board (kiff/tafs <c>docs/ios-app.md</c> › Illustrations),
/// ported from iOS's <c>Illustrations.swift</c> number for number: each drawn in code on its board's grid, in the theme's
/// colours so light and dark both read, and scaled down to the width it's given, never past its own (<c>shown</c>). Rule 14:
/// shapes only, never a letter, a number or a word; the screen's own shared words say what it means, so every drawing is
/// hidden from Narrator. Windows has no shadow under a card: a hairline in the theme's line colour lifts it instead.
/// </summary>
static class Illustrations
{
    /// <summary>Board 1, first run: a card opening, sparkles round it. Its button sits 32 down, not iOS's 40, so it fits inside
    /// the card: a Windows border clips what overflows.</summary>
    public static FrameworkElement FirstRun() => Make(380, 335, 200, d =>
    {
            d.Place(d.Halo(), 40, 10, 300, 300);
            d.Place(d.Card(24, 22, d.Mark(52), d.Bar(120, 0.85, top: 22), d.Bar(90, 0.18, top: 12), d.Bar(140, 0.18, top: 12),
                d.Pill(d.Tint("TafsAmber", 1), 40, top: 32)), 90, 40, 200, 250);
            d.Place(d.Dot(d.Tint("TafsAmberText", 0.6)), 10, 20, 18, 18);
            d.Place(d.Chip(d.Tint("TafsBlue", 0.55), 20), 350, 60, 16, 16);
            d.Place(d.Chip(Drawing.Pink(0.5), -15), 30, 310, 14, 14);
            d.Place(d.Dot(d.Tint("TafsAmberText", 0.35)), 330, 320, 20, 20);
    });

    /// <summary>Board 2, no tafs yet: an empty card's outline, a plus in it.</summary>
    public static FrameworkElement NoTafs() => Make(360, 320, 200, d =>
    {
            d.Place(d.Halo(), 30, 10, 300, 300);
            d.Place(d.DashedCard(24, d.Tint("TafsInk", 0.35), 4), 70, 60, 228, 208);
            d.Place(d.Dot(d.Tint("TafsAmber", 1)), 148, 128, 64, 64);
            d.Place(d.Capsule(Drawing.OnAmber), 177, 142, 6, 36);
            d.Place(d.Capsule(Drawing.OnAmber), 162, 157, 36, 6);
            d.Place(d.Sheet(14, 0.6), 10, 280, 120, 28);
            d.Place(d.Sheet(14, 0.6), 230, 280, 120, 28);
    });

    /// <summary>Board 3, a brief with nothing to decide: a calm sun over a clear page.</summary>
    public static FrameworkElement BriefClear() => Make(320, 430, 130, d =>
    {
            d.Place(d.Dot(d.Tint("TafsAmber", 0.9)), 100, 40, 120, 120);
            d.Place(d.Halo(), 70, 10, 180, 180);
            d.Place(d.Card(28, 22, d.Bar(140, 0.85), d.Row(d.Tint("TafsOk", 1), 150, top: 28), d.Row(d.Tint("TafsOk", 1), 120, top: 18)), 10, 200, 300, 220);
    });

    /// <summary>Board 4, every taf halted: cards at rest under a pause.</summary>
    public static FrameworkElement AllHalted() => Make(330, 320, 190, d =>
    {
            d.Place(d.Dot(d.Tint("TafsInk", 0.06)), 15, 10, 300, 300);
            d.Place(d.Turned(d.Sheet(22, 0.75), -6), 15, 90, 150, 190);
            d.Place(d.Turned(d.Sheet(22, 0.75), 5), 165, 80, 150, 190);
            // The mark greyed, as iOS's grayscale(0.7): the taf at rest.
            d.Place(d.Card(20, 22, d.Mark(40, fill: d.Tint("TafsInk", 0.3)), d.Bar(90, 0.3, top: 18), d.Bar(70, 0.15, top: 10)), 90, 65, 150, 200);
            d.Place(d.Dot(d.Tint("TafsInk", 1)), 205, 30, 76, 76);
            d.Place(d.Rounded(d.Tint("TafsOnFill", 1), 3), 229, 50, 10, 36);
            d.Place(d.Rounded(d.Tint("TafsOnFill", 1), 3), 247, 50, 10, 36);
    });

    /// <summary>Board 5, an app connected: two tiles joined, a check between them.</summary>
    public static FrameworkElement AppConnected() => Make(490, 215, 280, d =>
    {
            d.Place(d.Sheet(22), 15, 15, 180, 180);
            d.Place(d.Mark(90, radius: 24), 60, 60, 90, 90);
            d.Place(d.Sheet(22), 295, 15, 180, 180);
            d.Place(d.Outline(22, d.Tint("TafsInk", 1), 3), 295, 15, 180, 180);
            d.Place(d.Dot(d.Tint("TafsBlue", 0.85)), 345, 65, 80, 80);
            d.Place(d.Rule(d.Tint("TafsAmber", 1), 5, [12.0 / 5, 8.0 / 5]), 195, 101, 100, 5);
            d.Place(d.Dot(d.Tint("TafsAmberText", 1)), 213, 73, 64, 64);
            d.Place(d.Tick(d.Tint("TafsOnFill", 1), 4, 32), 229, 89, 32, 32);
    });

    /// <summary>Board 6, nothing waiting: an empty tray under a calm check.</summary>
    public static FrameworkElement NothingWaiting() => Make(380, 325, 200, d =>
    {
            d.Place(d.Halo(), 35, 10, 300, 300);
            d.Place(d.Sheet(26), 55, 160, 260, 150);
            d.Place(d.Dot(d.Tint("TafsOk", 1)), 145, 60, 80, 80);
            d.Place(d.Tick(d.Tint("TafsOnFill", 1), 5, 40), 165, 80, 40, 40);
            d.Place(d.Bar(140, 0.12), 115, 240, 140, 10);
            d.Place(d.Bar(90, 0.08), 140, 262, 90, 10);
            d.Place(d.Dot(d.Tint("TafsAmberText", 0.4)), 345, 90, 16, 16);
            d.Place(d.Dot(d.Tint("TafsBlue", 0.35)), 13, 210, 12, 12);
            d.Place(d.Dot(Drawing.Pink(0.35)), 360, 260, 10, 10);
    });

    /// <summary>Board 7, Coach with no taf or one: its spark beside one card, room for more.</summary>
    public static FrameworkElement CoachFirst() => Make(610, 280, 300, d =>
    {
            d.Place(d.Halo(), 10, 10, 260, 260);
            d.Place(d.Spark(Drawing.Brand), 70, 70, 140, 140);
            d.Place(d.Spark(d.Tint("TafsAmber", 0.6)), 215, 40, 40, 40);
            d.Place(d.Card(20, 22, d.Mark(40), d.Bar(90, 0.85, top: 18), d.Bar(70, 0.18, top: 10), d.Pill(d.Tint("TafsAmber", 0.25), 30, top: 30)), 310, 80, 150, 180);
            d.Place(d.DashedCard(22, d.Tint("TafsInk", 0.22), 4), 480, 95, 118, 158);
            d.Place(d.Rule(d.Tint("TafsAmber", 0.7), 5, [0, 1.8]), 250, 168, 50, 5);
    });

    /// <summary>Board 8, a new taf, small: words in a bubble becoming a card.</summary>
    public static FrameworkElement NewTaf() => Make(320, 235, 100, d =>
    {
            d.Place(d.Halo(), 60, 5, 220, 220);
            d.Place(d.Bubble(d.Bar(100, 0.7), d.Bar(70, 0.2, top: 10), d.Bar(86, 0.2, top: 10)), 10, 55, 150, 96);
            d.Place(d.Rule(d.Tint("TafsAmber", 1), 4, [0, 1.8]), 168, 100, 36, 4);
            d.Place(d.Card(14, 22, d.Mark(28, radius: 9), d.Bar(52, 0.8, height: 8, top: 12), d.Bar(40, 0.18, height: 8, top: 8)), 210, 55, 90, 116);
            d.Place(d.Spark(d.Tint("TafsAmber", 1)), 280, 37, 34, 34);
    });

    /// <summary>Board 9, a wide screen: Coach starting for the first time, its spark in rings filling.</summary>
    public static FrameworkElement CoachStarting() => Make(600, 490, 300, d =>
    {
            // Rings centred in a 300 box at (150, 10): 280 across, then 216, each with its arc from the top.
            d.Place(d.Ring(d.Tint("TafsInk", 0.07), 10), 160, 20, 280, 280);
            d.Place(d.Arc(d.Tint("TafsAmber", 1), 10, 0.625, 280), 160, 20, 280, 280);
            d.Place(d.Ring(d.Tint("TafsInk", 0.06), 8), 192, 52, 216, 216);
            d.Place(d.Arc(d.Tint("TafsBlue", 0.7), 8, 0.38, 216), 192, 52, 216, 216);
            d.Place(d.Spark(Drawing.Brand), 240, 100, 120, 120);
            d.Place(d.Faded(d.Step(d.Tint("TafsOk", 1), 0.15), 0.9), 20, 360, 170, 110);
            d.Place(d.Step(d.Tint("TafsAmber", 1), 0.15), 215, 360, 170, 110);
            d.Place(d.Faded(d.Step(d.Tint("TafsInk", 0.15), 0.1), 0.55), 410, 360, 170, 110);
            d.Place(d.Dot(Drawing.Pink(0.4)), 100, 70, 14, 14);
            d.Place(d.Dot(d.Tint("TafsAmberText", 0.4)), 490, 50, 18, 18);
    });

    /// <summary>Board 10, a wide screen: the overview, a ring, a gentle line and cards in a row.</summary>
    public static FrameworkElement Overview() => Make(860, 480, 340, d =>
    {
            d.Place(d.Sheet(22), 20, 20, 360, 260);
            d.Place(d.Ring(d.Tint("TafsInk", 0.07), 16), 140, 69, 120, 120);
            d.Place(d.Arc(Drawing.Brand, 16, 0.68, 120), 140, 69, 120, 120);
            d.Place(d.Bar(140, 0.15), 130, 226, 140, 10);
            d.Place(d.Sheet(22), 410, 20, 430, 260);
            d.Place(d.Bar(120, 0.8), 438, 48, 120, 10);
            d.Place(d.Rise(d.Tint("TafsAmber", 0.12), closed: true), 438, 82, 374, 160);
            d.Place(d.Rise(d.Tint("TafsAmber", 1), closed: false), 438, 82, 374, 160);
            d.Place(d.Dot(d.Tint("TafsAmberText", 1)), 438 + 250 - 10, 82 + 60 - 10, 20, 20);
            d.Place(d.Tile(d.Mark(38), 140, 100), 20, 310, 260, 150);
            d.Place(d.Tile(d.Dot(d.Tint("TafsOk", 1)), 120, 150), 300, 310, 260, 150);
            d.Place(d.Tile(d.Dot(d.Tint("TafsBlue", 0.85)), 130, 90), 580, 310, 260, 150);
    });

    /// <summary>
    /// A board, scaled to the room it's given and never wider than <paramref name="shown"/>, hidden from Narrator. It paints
    /// itself in its own theme, read from where it is: again once it's in the window, and whenever the theme changes, since
    /// a page draws it before it knows its theme.
    /// </summary>
    static FrameworkElement Make(double width, double height, double shown, Action<Drawing> paint)
    {
        var canvas = new Canvas { Width = width, Height = height };
        var box = new Viewbox { Child = canvas, MaxWidth = shown, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetAccessibilityView(box, AccessibilityView.Raw);
        void Paint()
        {
            canvas.Children.Clear();
            paint(new Drawing(key => ChatViews.ThemeBrush(box.ActualTheme, key), canvas));
        }
        Paint();
        box.Loaded += (_, _) => Paint();
        box.ActualThemeChanged += (_, _) => Paint();
        return box;
    }

    /// <summary>One board's canvas and its pieces, in the theme's brushes.</summary>
    sealed class Drawing(Func<string, Brush> look, Canvas canvas)
    {

        /// <summary>The app's mark, the brand's sweep, the same in both themes.</summary>
        public static Brush Brand => (Brush)Application.Current.Resources["TafsBrand"];

        /// <summary>What's drawn on amber: white, as the app writes on its amber (<c>TafsOnAmberText</c>), in both themes.</summary>
        public static Brush OnAmber => new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));

        /// <summary>iOS's pink, which Windows' theme has no key for: only ever a faint sparkle.</summary>
        public static Brush Pink(double opacity) => new SolidColorBrush(Color.FromArgb(255, 0xE5, 0x48, 0x8A)) { Opacity = opacity };

        /// <summary>A theme colour at an opacity: a brush of its own, so the theme's shared one is never changed.</summary>
        public Brush Tint(string key, double opacity) =>
            look(key) is SolidColorBrush b ? new SolidColorBrush(b.Color) { Opacity = opacity } : look(key);

        /// <summary>Where its board puts it: left, top, width and height, in the board's points.</summary>
        public void Place(FrameworkElement e, double x, double y, double w, double h)
        {
            (e.Width, e.Height) = (w, h);
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            canvas.Children.Add(e);
        }

        /// <summary>The soft amber glow behind a drawing.</summary>
        public FrameworkElement Halo() => Dot(Tint("TafsAmber", 0.16));

        public FrameworkElement Dot(Brush fill) => new Ellipse { Fill = fill };

        /// <summary>A bar with round ends: a Rectangle, whose radius is held to half its size, whatever size it's given.</summary>
        public FrameworkElement Capsule(Brush fill) => new Rectangle { Fill = fill, RadiusX = 1000, RadiusY = 1000 };

        public FrameworkElement Rounded(Brush fill, double radius) => new Rectangle { Fill = fill, RadiusX = radius, RadiusY = radius };

        /// <summary>A small tilted square, a sparkle.</summary>
        public FrameworkElement Chip(Brush fill, double degrees) => Turned(Rounded(fill, 4), degrees);

        public FrameworkElement Turned(FrameworkElement e, double degrees)
        {
            e.RenderTransformOrigin = new Point(0.5, 0.5);
            e.RenderTransform = new RotateTransform { Angle = degrees };
            return e;
        }

        public FrameworkElement Faded(FrameworkElement e, double opacity)
        {
            e.Opacity = opacity;
            return e;
        }

        /// <summary>A line of a card, as a bar: never its words.</summary>
        public FrameworkElement Bar(double w, double opacity, double height = 10, double top = 0) =>
            new Border { Width = w, Height = height, CornerRadius = new CornerRadius(height / 2), Background = Tint("TafsInk", opacity),
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, top, 0, 0) };

        /// <summary>A full-width capsule in a card, as its button.</summary>
        public FrameworkElement Pill(Brush fill, double height, double top) =>
            new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Background = fill, Margin = new Thickness(0, top, 0, 0) };

        /// <summary>The app's mark: the brand's sweep, as the sign-in draws it; or another fill.</summary>
        public FrameworkElement Mark(double size, double radius = 12, Brush? fill = null) =>
            new Border { Width = size, Height = size, CornerRadius = new CornerRadius(radius), Background = fill ?? Brand, HorizontalAlignment = HorizontalAlignment.Left };

        /// <summary>A card's ground, lifted off the screen by a hairline.</summary>
        public FrameworkElement Sheet(double radius, double opacity = 1) =>
            new Border { CornerRadius = new CornerRadius(radius), Background = look("TafsPanel"), BorderBrush = look("TafsLine"), BorderThickness = new Thickness(1), Opacity = opacity };

        /// <summary>A card, its pieces stacked from its top left.</summary>
        public FrameworkElement Card(double padding, double radius, params UIElement[] pieces)
        {
            var stack = new StackPanel();
            foreach (var piece in pieces) stack.Children.Add(piece);
            return new Border
            {
                CornerRadius = new CornerRadius(radius), Background = look("TafsPanel"), BorderBrush = look("TafsLine"), BorderThickness = new Thickness(1),
                Padding = new Thickness(padding), Child = stack,
            };
        }

        /// <summary>A dot and a line, side by side.</summary>
        public FrameworkElement Row(Brush dot, double line, double top)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, top, 0, 0) };
            row.Children.Add(new Ellipse { Width = 34, Height = 34, Fill = dot });
            var bar = Bar(line, 0.15);
            bar.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(bar);
            return row;
        }

        /// <summary>One of Coach's first steps, under its rings.</summary>
        public FrameworkElement Step(Brush dot, double line) =>
            Card(18, 22, new Ellipse { Width = 30, Height = 30, Fill = dot, HorizontalAlignment = HorizontalAlignment.Left }, Bar(110, line, top: 16));

        /// <summary>One of the overview's cards: a badge and two lines.</summary>
        public FrameworkElement Tile(FrameworkElement badge, double first, double second)
        {
            (badge.Width, badge.Height, badge.HorizontalAlignment) = (38, 38, HorizontalAlignment.Left);
            return Card(22, 22, badge, Bar(first, 0.75, top: 20), Bar(second, 0.15, top: 10));
        }

        /// <summary>A card's outline, dashed: the room for one more.</summary>
        public FrameworkElement DashedCard(double radius, Brush stroke, double thickness) =>
            new Rectangle { RadiusX = radius, RadiusY = radius, Stroke = stroke, StrokeThickness = thickness, StrokeDashArray = Dashes(10 / thickness, 7 / thickness) };

        /// <summary>A dash pattern, in strokes: a DoubleCollection made and filled here, as Windows wants it.</summary>
        static DoubleCollection Dashes(params double[] dashes)
        {
            var collection = new DoubleCollection();
            foreach (var x in dashes) collection.Add(x);
            return collection;
        }

        public FrameworkElement Outline(double radius, Brush stroke, double thickness) =>
            new Rectangle { RadiusX = radius, RadiusY = radius, Stroke = stroke, StrokeThickness = thickness };

        /// <summary>A speech bubble: round corners, its bottom left one nearly square, its lines inside.</summary>
        public FrameworkElement Bubble(params UIElement[] lines)
        {
            var stack = new StackPanel();
            foreach (var line in lines) stack.Children.Add(line);
            return new Border
            {
                CornerRadius = new CornerRadius(24, 24, 24, 6), Background = look("TafsPanel"), BorderBrush = look("TafsLine"),
                BorderThickness = new Thickness(1), Padding = new Thickness(20), Child = stack,
            };
        }

        /// <summary>A straight line across the middle of its frame, dashed or dotted (dashes in strokes, as Windows counts them).</summary>
        public FrameworkElement Rule(Brush stroke, double thickness, double[] dashes)
        {
            return new Line
            {
                X1 = 0, X2 = 1, Y1 = 0, Y2 = 0, Stretch = Stretch.Fill, Stroke = stroke, StrokeThickness = thickness, StrokeDashArray = Dashes(dashes),
                StrokeDashCap = PenLineCap.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, VerticalAlignment = VerticalAlignment.Center,
            };
        }

        /// <summary>A check, drawn: on a 24 by 24 grid, as the boards draw it.</summary>
        public FrameworkElement Tick(Brush stroke, double thickness, double size) =>
            new Polyline
            {
                Points = { new Point(5, 12.5), new Point(9.5, 17), new Point(19, 7.5) }, Stroke = stroke, StrokeThickness = thickness * 24 / size,
                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            }.Scaled();

        public FrameworkElement Ring(Brush stroke, double thickness) => new Ellipse { Stroke = stroke, StrokeThickness = thickness };

        /// <summary>A ring's arc from its top, clockwise, for <paramref name="fraction"/> of the way round.</summary>
        public FrameworkElement Arc(Brush stroke, double thickness, double fraction, double size)
        {
            var r = (size - thickness) / 2;
            var c = size / 2;
            var angle = fraction * 2 * Math.PI;
            var figure = new PathFigure { StartPoint = new Point(c, c - r) };
            figure.Segments.Add(new ArcSegment
            {
                Point = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle)), Size = new Size(r, r),
                SweepDirection = SweepDirection.Clockwise, IsLargeArc = fraction > 0.5,
            });
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = geometry, Stroke = stroke, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            };
        }

        /// <summary>Coach's spark: four curved points, on a 100 by 100 grid.</summary>
        public FrameworkElement Spark(Brush fill)
        {
            var figure = new PathFigure { StartPoint = new Point(50, 0), IsClosed = true };
            figure.Segments.Add(new BezierSegment { Point1 = new Point(54, 34), Point2 = new Point(66, 46), Point3 = new Point(100, 50) });
            figure.Segments.Add(new BezierSegment { Point1 = new Point(66, 54), Point2 = new Point(54, 66), Point3 = new Point(50, 100) });
            figure.Segments.Add(new BezierSegment { Point1 = new Point(46, 66), Point2 = new Point(34, 54), Point3 = new Point(0, 50) });
            figure.Segments.Add(new BezierSegment { Point1 = new Point(34, 46), Point2 = new Point(46, 34), Point3 = new Point(50, 0) });
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Fill = fill, Stretch = Stretch.Fill };
        }

        /// <summary>The overview's gentle rise, on a 374 by 160 grid; closed, the area under it.</summary>
        public FrameworkElement Rise(Brush brush, bool closed)
        {
            var figure = new PathFigure { StartPoint = new Point(0, 130), IsClosed = closed, IsFilled = closed };
            figure.Segments.Add(new BezierSegment { Point1 = new Point(50, 120), Point2 = new Point(70, 80), Point3 = new Point(120, 92) });
            figure.Segments.Add(new BezierSegment { Point1 = new Point(170, 104), Point2 = new Point(200, 40), Point3 = new Point(250, 60) });
            figure.Segments.Add(new BezierSegment { Point1 = new Point(300, 80), Point2 = new Point(330, 20), Point3 = new Point(374, 26) });
            if (closed)
            {
                figure.Segments.Add(new LineSegment { Point = new Point(374, 160) });
                figure.Segments.Add(new LineSegment { Point = new Point(0, 160) });
            }
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            var path = new Microsoft.UI.Xaml.Shapes.Path { Data = geometry };
            if (closed) path.Fill = brush;
            else (path.Stroke, path.StrokeThickness, path.StrokeStartLineCap, path.StrokeEndLineCap) = (brush, 6, PenLineCap.Round, PenLineCap.Round);
            return path;
        }
    }

    /// <summary>A tick drawn on its own 24-point grid, held in a frame the board sizes: the grid scales with it.</summary>
    static FrameworkElement Scaled(this Polyline tick)
    {
        var grid = new Canvas { Width = 24, Height = 24 };
        grid.Children.Add(tick);
        return new Viewbox { Child = grid, Stretch = Stretch.Fill };
    }
}
