using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Connections;
using Tafs.Core.Kiff;

namespace Tafs.App;

/// <summary>
/// Connecting an app from Connect apps (P9), for whoever manages a taf on a PC whose passkey approves: the tafs that need the
/// app, starting the connection for one (tafs cloud asks planque for the grant), the grant read as it is with KIFF's words for
/// its profile, and approving it (<see cref="ConnectFlow.ApproveAsync"/>).
/// </summary>
public sealed record ConnectAppsConnecting(IReadOnlyList<(string Id, string Name)> Tafs, Func<ConnectApp, string, Task<((string Grant, bool NeedsKey)? Started, string? Why)>> Start,
    Func<string, Task<(PlanqueGrant? Grant, ProfileWords? Words, string? Why)>> Read, Func<string, string, ProfileWords?, bool, Task<ConnectResult>> Approve,
    Func<string, Task<KeySheet?>>? Sheet = null, Func<string, string, ProfileWords?, string, Task<ConnectResult>>? GiveKey = null);

/// <summary>
/// The approval sheet for a sign-in at the vendor, as the iPad's <c>ConnectApprovalSheet.signIn</c>: "Connect Gmail", the
/// connection's own words as planque keeps them (what its digest covers), KIFF's words for its profile (what KIFF can't check in
/// amber), then Approve with Windows Hello. While it goes, the sheet stays, saying it's waiting for the browser; a grant that
/// changed meanwhile is shown again; why it didn't go stays on the sheet.
/// </summary>
public static class ConnectDialog
{
    /// <summary>
    /// Connected: true; else nothing changed. <paramref name="again"/>: Sign in again on a connection, the same sheet (what's
    /// approved is shown first, its words and KIFF's for its profile, so nothing added since is approved unseen), a connected
    /// one signed in again in place.
    /// </summary>
    public static async Task<bool> ShowAsync(XamlRoot root, ElementTheme theme, ConnectApp app, string grantId, ConnectAppsConnecting connecting,
        bool again = false)
    {
        var w = App.Words;
        var (grant, words, why) = await connecting.Read(grantId);
        var connected = false;
        var busy = false;
        var body = new StackPanel { Spacing = 10 };
        var dialog = new ContentDialog
        {
            XamlRoot = root, RequestedTheme = theme,
            Title = again ? $"{w.Get("connect.sign_in_again")} · {grant?.Vendor ?? app.DisplayName}" : w.Get("coach.connect_service", ("service", grant?.Vendor ?? app.DisplayName)),
            Content = new ScrollViewer { Content = body, MaxHeight = 520 },
            PrimaryButtonText = w.Get("approval.approve_with", ("biometry", Tafs.Core.Coach.CoachScreen.Biometry)),
            CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Primary,
        };
        void Draw()
        {
            body.Children.Clear();
            if (grant is null) body.Children.Add(Words(why ?? w.Get("connect.reading_request"), 14, why is null ? "TafsDimText" : "TafsNoText"));
            else
            {
                body.Children.Add(Words(grant.Text, 14, "TafsInkText"));
                foreach (var line in words?.Lines ?? [])
                    body.Children.Add(Words(line.Text, 13, line.Caution ? "TafsAmberWords" : "TafsSoftText"));
                if (why is not null) body.Children.Add(Words(why, 13, "TafsNoText"));
                if (busy) body.Children.Add(Words(w.Get("needs_you.waiting_for_browser"), 13, "TafsDimText"));
            }
            dialog.PrimaryButtonText = w.Get(busy ? "approval.approving" : "approval.approve_with", ("biometry", Tafs.Core.Coach.CoachScreen.Biometry));
            // A connection with a profile is approved only with KIFF's words for it shown.
            dialog.IsPrimaryButtonEnabled = !busy && grant is not null && (grant.Profile is null || words is not null) && ApprovalGate.Ready;
        }
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (busy || grant is null) return;
            var deferral = args.GetDeferral();
            (busy, why) = (true, null);
            Draw();
            try
            {
                var result = await connecting.Approve(grant.Id, grant.Digest, words, again);
                if (result.Connected)
                {
                    connected = true;
                    dialog.Hide();
                    return;
                }
                if (result.Changed is { } changed)
                {
                    // What's asked changed since it was shown: shown again, with KIFF's words read again.
                    (grant, words, why) = await connecting.Read(changed.Id);
                    why ??= w.Get("version.out_of_date_card");
                }
                else why = result.Why;
            }
            finally
            {
                busy = false;
                deferral.Complete();
                Draw();
            }
        };
        Draw();
        // Windows Hello not ready here: why, above the request, and Approve off until it is.
        ApprovalGate.Notice(dialog, Draw);
        await dialog.ShowAsync();
        return connected;
    }

    static TextBlock Words(string text, double size, string style) =>
        new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Style = (Style)Application.Current.Resources[style] };
}
