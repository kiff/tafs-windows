using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Connections;
using Tafs.Core.Kiff;

namespace Tafs.App;

/// <summary>
/// A key-based app's sheet (P9), as the iPad's <c>ConnectApprovalSheet.keySheet</c> and its receipt: tafs cloud's words for the
/// grant (its title, the three lines, what to paste), KIFF's words for its profile, Paste (the clipboard read on that click only,
/// then cleared; only the key's last 4 characters ever shown), the legal line, then Connect, which gives the key in KIFF's window
/// with Windows Hello. The receipt says it's connected with …last4. A "reads only" line that isn't true of the grant this PC
/// read shows nothing to paste or sign.
/// </summary>
public static class KeyDialog
{
    /// <summary>Connected: true; else nothing changed. The key is never kept past the sheet.</summary>
    public static async Task<bool> ShowAsync(XamlRoot root, ElementTheme theme, ConnectApp app, string grantId, ConnectAppsConnecting connecting)
    {
        var w = App.Words;
        var (grant, words, why) = await connecting.Read(grantId);
        var sheet = grant is null || connecting.Sheet is null ? null : await connecting.Sheet(grantId);
        string? key = null;
        var last4 = "";
        var (busy, given) = (false, false);
        var body = new StackPanel { Spacing = 12 };
        var dialog = new ContentDialog
        {
            XamlRoot = root, RequestedTheme = theme, Title = sheet?.Title ?? w.Get("coach.connect_service", ("service", grant?.Vendor ?? app.DisplayName)),
            Content = new ScrollViewer { Content = body, MaxHeight = 560 }, CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Primary,
        };
        void Draw()
        {
            body.Children.Clear();
            if (grant is null || sheet is null)
            {
                // tafs cloud's words can't be read: nothing to paste here (the app's own lines speak of a phone).
                body.Children.Add(Words(why ?? w.Get("connect.could_not_connected", ("vendor", app.DisplayName), ("why", w.Get("kiff.kiff_did_not_answer_expected"))), 14, "TafsNoText"));
                dialog.PrimaryButtonText = "";
                return;
            }
            if (sheet.Host is { Length: > 0 } host) body.Children.Add(Words("🔒 " + host, 13, "TafsSoftText"));
            if (given)
            {
                // The receipt: board 5, an app connected (the moment the key went in, as the iPad's), then connected with the
                // key's last 4, then Done.
                var drawing = Illustrations.AppConnected();
                drawing.HorizontalAlignment = HorizontalAlignment.Center;
                body.Children.Add(drawing);
                body.Children.Add(Words(sheet.Filled(sheet.Receipt ?? w.Get("connect.status.connected"), last4), 16, "TafsInkText"));
                dialog.PrimaryButtonText = "";
                dialog.CloseButtonText = w.Get("common.done");
                return;
            }
            if (!sheet.SaysTrue(grant))
            {
                body.Children.Add(Words(w.Get("connect.request_does_not_match_what"), 14, "TafsNoText"));
                dialog.PrimaryButtonText = "";
                return;
            }
            foreach (var line in sheet.Lines) body.Children.Add(Words(line.Text, 15, "TafsInkText"));
            foreach (var line in words?.Lines ?? []) body.Children.Add(Words(line.Text, 13, line.Caution ? "TafsAmberWords" : "TafsSoftText"));
            if (grant.Profile is not null && words is null) body.Children.Add(Words(w.Get("connect.profile_words_unread"), 13, "TafsSoftText"));
            if (!grant.DigestMatches) body.Children.Add(Words(w.Get("connect.request_does_not_match_what"), 13, "TafsNoText"));
            body.Children.Add(Words(sheet.PasteWhat, 15, "TafsInkText"));
            if (key is not null)
            {
                body.Children.Add(Words(sheet.Filled(sheet.Pasted, last4), 16, "TafsInkText"));
                body.Children.Add(Words(sheet.Cleared, 12, "TafsDimText"));
                if (sheet.Next?.Trim() is { Length: > 0 } next) body.Children.Add(Words(next, 14, "TafsInkText"));
            }
            var paste = new Button { Content = sheet.PasteButton, Style = (Style)Application.Current.Resources["TafsOutlineButton"], IsEnabled = !busy };
            AutomationProperties.SetAutomationId(paste, "connect.key.paste");
            paste.Click += async (_, _) =>
            {
                // The clipboard is read on this click only; then the copy is taken off it, where other apps could read it.
                var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                if (!content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)) return;
                if (KeySheet.Taken(await content.GetTextAsync()) is not { } taken) return;
                (key, last4) = (taken, KeySheet.Last4(taken));
                try { Windows.ApplicationModel.DataTransfer.Clipboard.Clear(); } catch (System.Runtime.InteropServices.COMException) { }
                Draw();
            };
            body.Children.Add(paste);
            if (sheet.LegalWords is { Length: > 0 } legal) body.Children.Add(Words(legal, 12, "TafsDimText"));
            if (why is not null) body.Children.Add(Words(why, 13, "TafsNoText"));
            var details = new StackPanel { Spacing = 8 };
            foreach (var (label, value) in sheet.Details)
            {
                details.Children.Add(Words(label, 12, "TafsDimText"));
                details.Children.Add(Words(value, 13, "TafsInkText"));
            }
            details.Children.Add(Words(grant.Text, 13, "TafsInkText"));
            body.Children.Add(new Expander { Header = sheet.DetailsTitle ?? w.Get("common.details"), Content = details,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left });
            dialog.PrimaryButtonText = busy ? w.Get("approval.approving") : sheet.Button;
            dialog.IsPrimaryButtonEnabled = !busy && key is not null && grant.DigestMatches && (grant.Profile is null || words is not null);
        }
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (busy || grant is null || key is not { } pasted) return;
            var deferral = args.GetDeferral();
            (busy, why) = (true, null);
            Draw();
            try
            {
                var result = await connecting.GiveKey!(grant.Id, grant.Digest, words, pasted);
                if (result.Connected) (given, key) = (true, null);
                else if (result.Changed is { } changed)
                {
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
        await dialog.ShowAsync();
        // The key is forgotten with the sheet.
        key = null;
        return given;
    }

    static TextBlock Words(string text, double size, string style) =>
        new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Style = (Style)Application.Current.Resources[style] };
}
