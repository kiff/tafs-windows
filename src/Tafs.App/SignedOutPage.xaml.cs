using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Documents;
using Tafs.Core.SignIn;

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

    /// <summary>The terms version in force, read as the tick shows: the one sent with the sign-in is the one the person saw.</summary>
    string? termsVersion;
    /// <summary>KIFF's sign-in is open in the browser, or its session is being made.</summary>
    bool signingIn;

    public SignedOutPage()
    {
        InitializeComponent();
        // Board 1 on the KIFF half, as iOS's welcome and the Mac's sign-in: the owner's approved art replaces the old drawing.
        KiffArt.Child = Illustrations.FirstRun();
        PairingPage.TermsOutdated += OnTermsOutdated;
        Unloaded += (_, _) =>
        {
            PairingPage.TermsOutdated -= OnTermsOutdated;
            PairingPage.TermsVersion = null;
            // The welcome is gone: its attempt's link back is never used.
            AppSignInFlow.Forget();
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        var w = App.Words;
        KiffHeading.Text = w.Get("sign_in.with_kiff");
        ContinueWords.Text = w.Get("common.continue");
        TrySample.Content = w.Get("sample.try_it");
        PhoneHeading.Text = w.Get("sign_in.with_phone");
        ShowCode.Content = w.Get("sign_in.show_code");
        ScanLine.Text = w.Get("sign_in.scan_with_tafs");
        DrawQrMarks();
        // The consent: one tick for both Terms, privacy as two links, the age line for this PC's region.
        Linked(TickWords, Onboarding.ConsentTick(w));
        Linked(PrivacyWords, Onboarding.PrivacyLinks(w));
        AutomationProperties.SetName(Tick, Onboarding.ConsentTickSaid(w));
        TermsChanged.Text = w.Get("onboarding.terms_changed");
        AgeLine.Text = Onboarding.AgeLine(w, MinimumAge.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "Strings")), Region());
        termsVersion = await AppSignInFlow.TermsAsync();
    }

    /// <summary>This PC's region, as Windows has it ("FR"); null when it isn't a country code.</summary>
    static string? Region()
    {
        try { return Windows.System.UserProfile.GlobalizationPreferences.HomeGeographicRegion is { Length: 2 } r ? r : null; }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>A line's words, each linked part a link to its page.</summary>
    static void Linked(RichTextBlock block, IReadOnlyList<(string Text, Uri? Link)> parts)
    {
        block.Blocks.Clear();
        var paragraph = new Paragraph();
        foreach (var (text, link) in parts)
        {
            if (link is null) { paragraph.Inlines.Add(new Run { Text = text }); continue; }
            var hyperlink = new Hyperlink { NavigateUri = link };
            hyperlink.Inlines.Add(new Run { Text = text });
            paragraph.Inlines.Add(hyperlink);
        }
        block.Blocks.Add(paragraph);
    }

    /// <summary>Ticked, Continue and Show the code are free; unticked, they wait again, and a pairing in progress stops.</summary>
    /// <summary>
    /// Try it with sample data, as the iPad's <c>startSample</c>: tafs starts again on the sample (<c>--sample</c>), which reads no
    /// one's account and sends nothing. Account's Clear the sample starts it again without it.
    /// </summary>
    void OnTrySample(object sender, RoutedEventArgs e) => Microsoft.Windows.AppLifecycle.AppInstance.Restart(
        string.Join(' ', Environment.GetCommandLineArgs().Skip(1).Where(a => a != "--sample").Append("--sample")));

    void OnTicked(object sender, RoutedEventArgs e)
    {
        var ticked = Tick.IsChecked == true;
        if (ticked) TermsChanged.Visibility = Visibility.Collapsed;
        else Stop(RightFrame, RightIntro);
        Continue.IsEnabled = ticked && !signingIn;
        ShowCode.IsEnabled = ticked && !signingIn;
    }

    /// <summary>
    /// Continue: KIFF's sign-in in the browser (<c>AppSignInFlow</c>), then the celebration and the three cards. Declining
    /// leaves the welcome as it was; terms that changed since the tick untick the box.
    /// </summary>
    async void OnContinue(object sender, RoutedEventArgs e)
    {
        if (Tick.IsChecked != true || signingIn) return;
        // One at a time: the phone's half stops, its claim with it, so two sign-ins never land together.
        Stop(RightFrame, RightIntro);
        Problem.Visibility = Visibility.Collapsed;
        TermsChanged.Visibility = Visibility.Collapsed;
        termsVersion ??= await AppSignInFlow.TermsAsync();
        // Without the version in force nothing is sent: consent is never skipped.
        if (termsVersion is null) { Say(App.Words.Get("sign_in.didnt_work")); return; }
        Busy(true);
        await AppSignInFlow.BeginAsync(App.Words.Language, termsVersion, ended => DispatcherQueue.TryEnqueue(() => OnEnded(ended)));
    }

    async void OnEnded(AppSignInFlow.Ended ended)
    {
        Busy(false);
        if (ended.Session is { } session) { SignInPage.Announce(session); return; }
        switch (ended.Outcome)
        {
            case Tafs.Core.SignIn.AppSignIn.Outcome.Declined: break;
            case Tafs.Core.SignIn.AppSignIn.Outcome.NotOn: Say(ended.Words ?? App.Words.Get("sign_in.in_app_not_on")); break;
            case Tafs.Core.SignIn.AppSignIn.Outcome.TermsChanged: await UntickAsync(); break;
            default: Say(App.Words.Get("sign_in.didnt_work")); break;
        }
    }

    /// <summary>The terms changed since the tick: unticked, said, and the new version read for the new tick.</summary>
    async Task UntickAsync()
    {
        Tick.IsChecked = false;
        TermsChanged.Visibility = Visibility.Visible;
        termsVersion = await AppSignInFlow.TermsAsync();
    }

    void OnTermsOutdated() => DispatcherQueue.TryEnqueue(() => _ = UntickAsync());

    void Say(string why)
    {
        Problem.Text = why;
        Problem.Visibility = Visibility.Visible;
    }

    void Busy(bool busy)
    {
        signingIn = busy;
        ContinueWords.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        ContinueBusy.IsActive = busy;
        ContinueBusy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Continue.IsEnabled = !busy && Tick.IsChecked == true;
        ShowCode.IsEnabled = !busy && Tick.IsChecked == true;
    }

    /// <summary>
    /// Show the code: the pairing page in this half, which asks Windows Hello once for this PC's passkey, then shows the QR.
    /// The claim carries the version the tick agreed to, read now if it couldn't be when the welcome showed; without it,
    /// nothing opens.
    /// </summary>
    async void OnShowCode(object sender, RoutedEventArgs e)
    {
        if (Tick.IsChecked != true) return;
        Problem.Visibility = Visibility.Collapsed;
        termsVersion ??= await AppSignInFlow.TermsAsync();
        if (termsVersion is null) { Say(App.Words.Get("sign_in.didnt_work")); return; }
        // One at a time: KIFF's sign-in in the browser is forgotten, so two sign-ins never land together.
        AppSignInFlow.Forget();
        Busy(false);
        PairingPage.TermsVersion = termsVersion;
        Stop(LeftFrame, LeftIntro);
        RightIntro.Visibility = Visibility.Collapsed;
        RightFrame.Visibility = Visibility.Visible;
        RightFrame.Navigate(typeof(PairingPage));
    }

    /// <summary>A half back to its start: its page goes (it cancels what it was waiting for as it unloads), and its button shows again.</summary>
    static void Stop(Frame half, UIElement intro)
    {
        if (half.Content is null) return;
        half.Content = null;
        half.BackStack.Clear();
        half.Visibility = Visibility.Collapsed;
        intro.Visibility = Visibility.Visible;
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
