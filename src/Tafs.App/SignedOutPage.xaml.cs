using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

namespace Tafs.App;

/// <summary>
/// Signed out, the whole window, as the owner's approved mockup and the iPad's <c>SignInView</c> on a computer: Sign in with
/// KIFF on the left, Sign in with your phone on the right. On Windows, the phone's half waits for Show the code, so Windows
/// Hello asks only on that tap, never as the window opens. A narrow window stacks the halves.
/// </summary>
public sealed partial class SignedOutPage : Page
{
    /// <summary>Under this width, the halves stack, as the mockup's narrow window.</summary>
    const double Narrow = 860;

    public SignedOutPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var w = App.Words;
        KiffHeading.Text = w.Get("sign_in.with_kiff");
        Continue.Content = w.Get("common.continue");
        PhoneHeading.Text = w.Get("sign_in.with_phone");
        ShowCode.Content = w.Get("sign_in.show_code");
        ScanLine.Text = w.Get("sign_in.scan_with_tafs");
        DrawQrMarks();
    }

    /// <summary>Continue: KIFF's device code, in this half, as the sign-in page starts it.</summary>
    void OnContinue(object sender, RoutedEventArgs e)
    {
        LeftIntro.Visibility = Visibility.Collapsed;
        LeftFrame.Visibility = Visibility.Visible;
        LeftFrame.Navigate(typeof(SignInPage), SignInPage.StartNow);
    }

    /// <summary>Show the code: the pairing page in this half, which asks Windows Hello once for this PC's passkey, then shows the QR.</summary>
    void OnShowCode(object sender, RoutedEventArgs e)
    {
        RightIntro.Visibility = Visibility.Collapsed;
        RightFrame.Visibility = Visibility.Visible;
        RightFrame.Navigate(typeof(PairingPage));
    }

    /// <summary>Side by side, or stacked under a narrow width, as the mockup's two windows.</summary>
    void OnSized(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < Narrow;
        LeftColumn.Width = new GridLength(1, GridUnitType.Star);
        RightColumn.Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        BottomRow.Height = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(Right, stacked ? 0 : 1);
        Grid.SetRow(Right, stacked ? 1 : 0);
        Divider.Visibility = stacked ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The drawing's code: a fixed pattern of squares, a picture of a code, never one that scans.</summary>
    void DrawQrMarks()
    {
        if (QrMarks.Children.Count > 0) return;
        string[] rows =
        [
            "1110111", "1010101", "1110111", "0001100", "1011011", "0101010", "1101101", "1011011",
        ];
        for (var y = 0; y < rows.Length; y++)
            for (var x = 0; x < rows[y].Length; x++)
            {
                if (rows[y][x] != '1') continue;
                var mark = new Rectangle { Width = 20, Height = 20, RadiusX = 3, RadiusY = 3, Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1B, 0x17, 0x26)) };
                Canvas.SetLeft(mark, x * 26);
                Canvas.SetTop(mark, y * 23);
                QrMarks.Children.Add(mark);
            }
    }
}
