using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Approvals;
using Tafs.Core.NeedsYou;

namespace Tafs.App;

/// <summary>
/// Whether Windows Hello can confirm on this PC, the app's one value, as the iPad's <c>approvalReadiness</c>: asked at launch,
/// whenever the window comes back (Hello may have just been set up in Settings), and on Check again, which answers for every
/// gate at once.
/// </summary>
public static class HelloState
{
    /// <summary>As last asked; ready until asked, so nothing is gated before Windows answers.</summary>
    public static HelloReadiness Readiness { get; private set; } = HelloReadiness.Ready;

    /// <summary>Each gate draws itself again with the new value.</summary>
    public static event Action? Changed;

    /// <summary>Asks Windows (<c>UserConsentVerifier.CheckAvailabilityAsync</c>); a Windows that doesn't say leaves every approval to say what came of it.</summary>
    public static async Task CheckAsync()
    {
        HelloReadiness now;
        try
        {
            now = await Windows.Security.Credentials.UI.UserConsentVerifier.CheckAvailabilityAsync() switch
            {
                Windows.Security.Credentials.UI.UserConsentVerifierAvailability.Available => HelloReadiness.Ready,
                Windows.Security.Credentials.UI.UserConsentVerifierAvailability.NotConfiguredForUser => HelloReadiness.NotSetUp,
                Windows.Security.Credentials.UI.UserConsentVerifierAvailability.DeviceBusy => HelloReadiness.Busy,
                _ => HelloReadiness.Unsupported,
            };
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException) { now = HelloReadiness.Ready; }
        // Only a change redraws the gates: the window is activated again each time Windows Hello's own prompt closes, in the
        // middle of the approval that asked for it, and nothing must move then.
        if (now == Readiness) return;
        Readiness = now;
        Changed?.Invoke();
    }

    /// <summary>A sign-out: ready again until asked for the next session.</summary>
    public static void Forget()
    {
        if (Readiness == HelloReadiness.Ready) return;
        Readiness = HelloReadiness.Ready;
        Changed?.Invoke();
    }
}

/// <summary>
/// An approval's button, or, while Windows Hello can't confirm here, why and what to do in its place, as the iPad's
/// <c>ApprovalGate</c> (a button where it stands) and <c>ApprovalNotice</c> (a dialog's own button, kept and turned off).
/// </summary>
public static class ApprovalGate
{
    /// <summary>The button while Hello is ready; else <see cref="HelloGate.NotReady"/>'s line, Open Settings and Check again. It follows each check.</summary>
    public static FrameworkElement Wrap(FrameworkElement approve)
    {
        var host = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };
        // A button that's hidden stays hidden: the gate only stands where the approval would show.
        void Draw() => host.Content = approve.Visibility == Visibility.Visible && HelloGate.NotReady(App.Words, HelloState.Readiness) is { } notReady
            ? NotReadyView(notReady) : approve;
        approve.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => Draw());
        // A button already laid out: the gate takes its place there, and the button moves into the gate.
        if (approve.Parent is Panel before)
        {
            var at = before.Children.IndexOf(approve);
            before.Children.RemoveAt(at);
            before.Children.Insert(at, host);
            (host.Margin, host.HorizontalAlignment) = (approve.Margin, approve.HorizontalAlignment);
            Grid.SetRow(host, Grid.GetRow(approve));
            Grid.SetColumn(host, Grid.GetColumn(approve));
            Grid.SetColumnSpan(host, Grid.GetColumnSpan(approve));
            approve.Margin = new Thickness(0);
        }
        Draw();
        void OnChanged() => host.DispatcherQueue?.TryEnqueue(Draw);
        HelloState.Changed += OnChanged;
        host.Unloaded += (_, _) => HelloState.Changed -= OnChanged;
        host.Loaded += (_, _) => { HelloState.Changed -= OnChanged; HelloState.Changed += OnChanged; Draw(); };
        return host;
    }

    /// <summary>Whether an approval can be asked now: Windows Hello ready, or not asked yet.</summary>
    public static bool Ready => HelloGate.NotReady(App.Words, HelloState.Readiness) is null;

    /// <summary>
    /// A dialog whose primary button approves, as the iPad's <c>ApprovalNotice</c>: while Hello can't confirm, the line and its
    /// buttons above the dialog's content, and the primary button off until a check finds it ready. The notice only ever turns
    /// the button off; it turns it back on only where it was the one that turned it off, so a dialog busy approving (its button
    /// off while it signs) is never turned back on by a check. A dialog that turns its button on and off itself passes its own
    /// <paramref name="redraw"/>, which reads <see cref="Ready"/> and its own busy state; it's called again with each change.
    /// </summary>
    public static void Notice(ContentDialog dialog, Action? redraw = null)
    {
        var notice = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var content = new StackPanel { Spacing = 12, Children = { notice } };
        if (dialog.Content is UIElement before) content.Children.Add(before);
        dialog.Content = content;
        var offByNotice = false;
        void Draw()
        {
            var notReady = HelloGate.NotReady(App.Words, HelloState.Readiness);
            notice.Content = notReady is null ? null : NotReadyView(notReady);
            notice.Visibility = notReady is null ? Visibility.Collapsed : Visibility.Visible;
            if (redraw is not null) redraw();
            else if (notReady is not null && dialog.IsPrimaryButtonEnabled) (dialog.IsPrimaryButtonEnabled, offByNotice) = (false, true);
            else if (notReady is null && offByNotice) (dialog.IsPrimaryButtonEnabled, offByNotice) = (true, false);
        }
        Draw();
        void OnChanged() => dialog.DispatcherQueue?.TryEnqueue(Draw);
        HelloState.Changed += OnChanged;
        dialog.Closed += (_, _) => HelloState.Changed -= OnChanged;
    }

    /// <summary>
    /// Hello not ready, as the iPad's <c>ApprovalNotReady</c>: the line, then Open Settings (Windows' sign-in options) where
    /// setting Hello up is the way, and Check again unless this PC has no Hello it can use.
    /// </summary>
    public static UIElement NotReadyView(HelloNotReady notReady)
    {
        var w = App.Words;
        var box = new StackPanel { Spacing = 8 };
        var line = new TextBlock { Text = notReady.Line, FontSize = 14, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["TafsInkText"] };
        AutomationProperties.SetAutomationId(line, "approvalNotReady");
        box.Children.Add(line);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (notReady.OpensSettings)
        {
            var open = new Button { Content = w.Get("voice.open_settings"), Style = (Style)Application.Current.Resources["TafsPrimary"] };
            open.Click += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(YourSteps.HelloSettings);
            buttons.Children.Add(open);
        }
        if (notReady.ChecksAgain)
        {
            var again = new Button { Content = w.Get("approval.check_again"), Style = (Style)Application.Current.Resources["TafsOutlineButton"] };
            again.Click += (_, _) => _ = HelloState.CheckAsync();
            buttons.Children.Add(again);
        }
        if (buttons.Children.Count > 0) box.Children.Add(buttons);
        return box;
    }
}
