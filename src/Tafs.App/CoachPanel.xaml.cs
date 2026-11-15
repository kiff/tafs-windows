using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Tafs.Core.Coach;
using Windows.UI.ViewManagement;

namespace Tafs.App;

/// <summary>Coach mode's panel. Its conversation (tafs cloud's Setup) comes in its own change: until then, the intro.</summary>
public sealed partial class CoachPanel : UserControl
{
    /// <summary>The iPad's shadow reaches 24 past the panel's edge: a slide goes that much further, so none of it stays.</summary>
    const double ShadowReach = 24;

    /// <summary>After it has gone: the window gives focus back to what opened it.</summary>
    public event Action? Closed;

    public CoachPanel()
    {
        InitializeComponent();
        var w = App.Words;
        var intro = CoachScreen.Intro(w);
        TitleText.Text = intro.Title;
        Heading.Text = intro.Heading;
        LineText.Text = intro.Line;
        Start.Content = intro.Start;
        Footnote.Text = intro.Footnote;
        AutomationProperties.SetName(Back, w.Get("common.back"));
        AutomationProperties.SetName(FullScreen, w.Get("coach.full_screen"));
        AutomationProperties.SetName(Backdrop, w.Get("coach.close_coach_mode"));
        KeyboardAccelerators.Add(Escape());
    }

    static CoachMotion Motion => CoachScreen.Motion(new UISettings().AnimationsEnabled);

    bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>While it's sliding out: still there, but going.</summary>
    bool closing;

    /// <summary>It slides in from the right (fades, with Windows' animations off), and focus moves to Back.</summary>
    /// <param name="animate">False to be there at once, as for a screenshot.</param>
    public void Open(bool animate = true)
    {
        if (closing)
        {
            // Opened again while it's going: it comes back.
            closing = false;
            Animate(0, 1, 1);
            return;
        }
        if (IsOpen) return;
        Fit(WindowWidth);
        var slide = animate && Motion == CoachMotion.Slide;
        var fade = animate && Motion == CoachMotion.Fade;
        Hold(slide ? Off : 0, fade ? 0 : 1, animate ? 0 : 1);
        Visibility = Visibility.Visible;
        if (animate) Animate(0, 1, 1);
        DispatcherQueue.TryEnqueue(() => Back.Focus(FocusState.Programmatic));
    }

    /// <summary>It slides back out to the right (or fades), then tells the window it has gone.</summary>
    public void Close()
    {
        if (!IsOpen || closing) return;
        closing = true;
        Animate(Motion == CoachMotion.Slide ? Off : 0, Motion == CoachMotion.Fade ? 0 : 1, 0, () =>
        {
            closing = false;
            Visibility = Visibility.Collapsed;
            Hold(0, 1, 1);
            Closed?.Invoke();
        });
    }

    void OnBack(object sender, RoutedEventArgs e) => Close();

    bool full;

    /// <summary>⤢ makes it full screen, and brings it back to a third: its edge moves, as the iPad's does.</summary>
    void OnFull(object sender, RoutedEventArgs e)
    {
        var before = PanelColumn.Width.Value;
        full = !full;
        FullIcon.Glyph = full ? "" : "";
        AutomationProperties.SetName(FullScreen, App.Words.Get(full ? "coach.back_width" : "coach.full_screen"));
        var after = WidthIn(Over.ActualWidth);
        if (Motion == CoachMotion.Fade)
        {
            Fit(Over.ActualWidth);
            return;
        }
        if (full)
        {
            // Wide at once, its edge where it was, then the edge moves to the left.
            Fit(Over.ActualWidth);
            Hold(after - before, 1, 1);
            Animate(0, 1, 1);
        }
        else
        {
            // The edge moves to the right, then it's a third wide.
            Animate(before - after, 1, 1, () =>
            {
                Fit(Over.ActualWidth);
                Hold(0, 1, 1);
            });
        }
    }

    void OnBackdrop(object sender, TappedRoutedEventArgs e) => Close();

    void OnSized(object sender, SizeChangedEventArgs e)
    {
        if (running is null) Fit(e.NewSize.Width);
    }

    double WidthIn(double window) => full ? window : CoachScreen.PanelWidth(window);

    void Fit(double window) => PanelColumn.Width = new GridLength(WidthIn(window));

    double WindowWidth => (Parent as FrameworkElement)?.ActualWidth ?? Over.ActualWidth;

    /// <summary>Off past the right edge: the panel's width and its shadow.</summary>
    double Off => PanelColumn.Width.Value + ShadowReach;

    Storyboard? running;

    /// <summary>Where it rests: how far to the right, how opaque the panel is, and the backdrop.</summary>
    void Hold(double slide, double panel, double backdrop)
    {
        Slide.X = slide;
        Panel.Opacity = panel;
        Backdrop.Opacity = backdrop;
    }

    /// <summary>
    /// Moves to where it rests next, easing out over 0.22 s. A newer move takes over: the one before stops where it
    /// rested, so nothing it held stays behind.
    /// </summary>
    void Animate(double slide, double panel, double backdrop, Action? then = null)
    {
        running?.Stop();
        var sb = new Storyboard();
        To(sb, Slide, "X", slide);
        To(sb, Panel, "Opacity", panel);
        To(sb, Backdrop, "Opacity", backdrop);
        sb.Completed += (_, _) =>
        {
            if (running != sb) return;
            running = null;
            Hold(slide, panel, backdrop);
            sb.Stop();
            then?.Invoke();
        };
        running = sb;
        sb.Begin();
    }

    static void To(Storyboard sb, DependencyObject target, string property, double to)
    {
        var a = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(CoachScreen.MotionTime),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(a, target);
        Storyboard.SetTargetProperty(a, property);
        sb.Children.Add(a);
    }

    /// <summary>Escape closes it, as Close does.</summary>
    KeyboardAccelerator Escape()
    {
        var k = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
        k.Invoked += (_, a) => { Close(); a.Handled = true; };
        return k;
    }
}
