using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tafs.Core.Connections;
using Tafs.Core.Taf;

namespace Tafs.App;

public sealed partial class TafPage
{
    /// <summary>The grant being signed in again from this page: one at a time, and the button says it's opening meanwhile.</summary>
    string? connectingAgain;
    /// <summary>tafs cloud's words once this taf's connection is connected again (the iPad's <c>connectedAgain</c>), by taf.</summary>
    readonly Dictionary<string, string> connectedAgain = [];
    /// <summary>Why a Connect again from this page didn't start, by grant (the iPad's <c>reconnectErrors</c>).</summary>
    readonly Dictionary<string, string> reconnectWhy = [];

    /// <summary>
    /// A taf's own Connect again, first in Setup, as the iPad's <c>ConnectAgainPanel</c>: tafs cloud's words in amber and its
    /// button, which signs that same connection in again here (what's approved shown first, then Windows Hello, then the vendor's
    /// own page), never a trip through every app. For whoever manages the taf, on a PC that approves. Once it's connected, the
    /// panel gives way to tafs cloud's words for it, with a ✓.
    /// </summary>
    void AddConnectAgain(SetupPage? page)
    {
        if (opening is not { CloudId: { } taf } o) return;
        var w = App.Words;
        if (page?.ConnectAgain is { } again && o.Manages && o.Connecting is not null)
        {
            var words = Words(again.Words, 14, "TafsAmberWords");
            words.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            var button = new Button
            {
                Content = connectingAgain == again.Grant ? w.Get("connect.opening_sign_in") : again.Title,
                Style = Look("TafsPrimary"), IsEnabled = connectingAgain is null, HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetAutomationId(button, "setup.connect-again");
            button.Click += (_, _) => _ = ConnectAgainAsync(taf, again);
            var panel = Panel(words, button);
            if (reconnectWhy.GetValueOrDefault(again.Grant) is { } why) ((StackPanel)panel.Child).Children.Add(Words(why, 13, "TafsNoText"));
            // The iPad's Panel(stroke: amber at half).
            if (Application.Current.Resources["TafsAmber"] is SolidColorBrush amber)
                (panel.BorderBrush, panel.BorderThickness) = (new SolidColorBrush(amber.Color) { Opacity = 0.5 }, new Thickness(1));
            SetupPane.Children.Add(panel);
        }
        else if (connectedAgain.GetValueOrDefault(taf) is { } done)
        {
            var said = Words(done, 14, "TafsOkText");
            said.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            var line = new Grid { ColumnSpacing = 8 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            // The ✓ in the line's own green, as the iPad's checkmark.circle.fill.
            var tick = new FontIcon { Glyph = "", FontSize = 16, Foreground = said.Foreground, VerticalAlignment = VerticalAlignment.Top };
            AutomationProperties.SetAccessibilityView(tick, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            line.Children.Add(tick);
            Grid.SetColumn(said, 1);
            line.Children.Add(said);
            AutomationProperties.SetAutomationId(line, "setup.connected-again");
            SetupPane.Children.Add(line);
        }
    }

    /// <summary>
    /// Connect again, as the iPad's <c>connectAgain</c>: that connection's sheet (a provider's connection says why it can't), signed
    /// in again with connect-fresh where tafs cloud or planque says so. Connected, Setup is read again, after a fresh sign-in naming
    /// that connection so tafs cloud can say which account it went to, and the page says so.
    /// </summary>
    async Task ConnectAgainAsync(string taf, ConnectAgain again)
    {
        if (connectingAgain is not null || opening?.Connecting is not { } connecting) return;
        var mine = opening;
        connectedAgain.Remove(taf);
        reconnectWhy.Remove(again.Grant);
        connectingAgain = again.Grant;
        ShowSetup();
        var connected = false;
        try
        {
            // A provider's connection is removed and connected again instead: planque would refuse, so nothing is asked.
            var (grant, _, _) = await connecting.Read(again.Grant);
            if (opening != mine) return;
            if (grant is { SignsInThroughProvider: true })
            {
                reconnectWhy[again.Grant] = App.Words.Get("connect.through_provider", ("vendor", grant.Vendor));
                return;
            }
            var app = new ConnectApp(again.Grant) { Name = again.App.Length > 0 ? again.App : null, Title = again.App };
            connected = await ConnectDialog.ShowAsync(XamlRoot, ActualTheme, app, again.Grant, connecting, again: true, fresh: again.Fresh);
        }
        finally
        {
            if (opening == mine) connectingAgain = null;
        }
        if (opening != mine) return;
        if (!connected)
        {
            ShowSetup();
            return;
        }
        // Read again: the panel goes once tafs cloud no longer asks, and after a fresh sign-in it names where it went.
        if (opening is { CloudId: { } id, Read: { } read })
        {
            var kept = setups.Get(read, id, () => new SetupRead());
            setupRead = kept;
            await kept.LoadAsync(id, opening.ReadWithProof ?? ((path, _) => read(path)), opening.SetupProof,
                connectedAgain: again.Fresh ? again.Grant : null);
            if (opening != mine) return;
            if (again.Done(true, kept.Page?.ConnectedAgain) is { } words) connectedAgain[taf] = words;
        }
        if (tab == TafTab.Setup) ShowSetup();
    }
}
