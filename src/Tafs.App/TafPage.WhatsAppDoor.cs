using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Home;
using Tafs.Core.Taf;

namespace Tafs.App;

public sealed partial class TafPage
{
    /// <summary>
    /// Adding a WhatsApp door, as the iPad's <c>DoorsModel.add</c>: tafs cloud's sign-up page in the browser, with this
    /// attempt's one-use state; "Waiting for your browser…" with Cancel; then, from the page's return carrying that state, for
    /// this taf, within 15 minutes, tafs cloud makes the door with Meta's code, as the session, and it opens. Any other
    /// return is dropped; none in time, or Cancel, adds nothing.
    /// </summary>
    async Task AddWhatsAppDoorAsync(DoorApp app, string tafId)
    {
        var w = App.Words;
        var state = WhatsAppDoor.NewState();
        var opened = DateTimeOffset.UtcNow;
        var back = new TaskCompletionSource<WhatsAppSignedUp?>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Returned(WhatsAppSignedUp signedUp)
        {
            if (WhatsAppDoor.Takes(signedUp, tafId, state, opened, DateTimeOffset.UtcNow)) back.TrySetResult(signedUp);
        }
        var waiting = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = app.Label, CloseButtonText = w.Get("common.cancel"),
            Content = new TextBlock { Text = w.Get("needs_you.waiting_for_browser"), TextWrapping = TextWrapping.Wrap },
        };
        App.WhatsAppReturned += Returned;
        try
        {
            if (!await Windows.System.Launcher.LaunchUriAsync(WhatsAppDoor.SignupPage(HomeList.Api, tafId, state))) return;
            var cancelled = waiting.ShowAsync().AsTask();
            var done = await Task.WhenAny(back.Task, cancelled, Task.Delay(WhatsAppDoor.Wait));
            if (done == back.Task) waiting.Hide();
            if (done != back.Task || back.Task.Result is not { } signedUp)
            {
                // Cancel says nothing; the wait running out says the sign-up didn't finish.
                if (done != cancelled)
                {
                    waiting.Hide();
                    doorsWhy = w.Get("door.whatsapp_signup_did_not_finish");
                    ShowSetup();
                }
                return;
            }
            if (await DoorChangeAsync(HttpMethod.Post, TafDoors.Path(tafId), WhatsAppDoor.DoorBody(app.Id, signedUp))
                && doors?.Doors.LastOrDefault(x => x.Kind == app.Kind) is { } added)
                (doorShown, setupScreen) = (added.Id, SetupScreen.Door);
            ShowSetup();
        }
        finally { App.WhatsAppReturned -= Returned; }
    }
}
