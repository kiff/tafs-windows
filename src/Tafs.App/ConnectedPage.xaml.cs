using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Tafs.App;

/// <summary>
/// The celebration, as the iPad's <c>ConnectedView</c>: either way of signing in ends here, over the app, and Let's go opens
/// it. Never for the sample or a screenshot of another screen.
/// </summary>
public sealed partial class ConnectedPage : Page
{
    /// <summary>Let's go: the window opens the app.</summary>
    public static event Action? Done;

    public ConnectedPage()
    {
        InitializeComponent();
        SizeChanged += (_, e) => PlaceDots(e.NewSize);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var w = App.Words;
        Connected.Text = w.Get("sign_in.connected");
        Together.Text = w.Get("sign_in.connected_line");
        LetsGo.Content = w.Get("sign_in.lets_go");
        DispatcherQueue.TryEnqueue(() => LetsGo.Focus(FocusState.Programmatic));
    }

    void OnLetsGo(object sender, RoutedEventArgs e) => Done?.Invoke();

    /// <summary>The soft shapes, at the mockup's places across the window.</summary>
    void PlaceDots(Windows.Foundation.Size size)
    {
        (FrameworkElement Dot, double X, double Y)[] at =
        [
            (Dot1, 0.13, 0.16), (Dot2, 0.82, 0.19), (Dot3, 0.88, 0.5), (Dot4, 0.2, 0.77), (Dot5, 0.77, 0.73),
        ];
        foreach (var (dot, x, y) in at)
        {
            Canvas.SetLeft(dot, size.Width * x);
            Canvas.SetTop(dot, size.Height * y);
        }
    }
}
