using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Tafs.Core.Coach;

namespace Tafs.App;

/// <summary>Coach mode's panel. Its conversation (tafs cloud's Setup) comes in its own change: until then, the intro.</summary>
public sealed partial class CoachPanel : UserControl
{
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

    public void Open() => Visibility = Visibility.Visible;

    public void Close() => Visibility = Visibility.Collapsed;

    void OnBack(object sender, RoutedEventArgs e) => Close();

    bool full;

    /// <summary>⤢ makes it full screen, and brings it back to a third.</summary>
    void OnFull(object sender, RoutedEventArgs e)
    {
        full = !full;
        FullIcon.Glyph = full ? "" : "";
        AutomationProperties.SetName(FullScreen, App.Words.Get(full ? "coach.back_width" : "coach.full_screen"));
        PanelColumn.Width = new GridLength(full ? Over.ActualWidth : CoachScreen.PanelWidth(Over.ActualWidth));
    }

    void OnBackdrop(object sender, TappedRoutedEventArgs e) => Close();

    void OnSized(object sender, SizeChangedEventArgs e) =>
        PanelColumn.Width = new GridLength(full ? e.NewSize.Width : CoachScreen.PanelWidth(e.NewSize.Width));

    /// <summary>Escape closes it, as Close does.</summary>
    KeyboardAccelerator Escape()
    {
        var k = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
        k.Invoked += (_, a) => { Close(); a.Handled = true; };
        return k;
    }
}
