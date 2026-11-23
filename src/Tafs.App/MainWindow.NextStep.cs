using Tafs.Core.Connections;
using Tafs.Core.Home;
using Tafs.Core.Taf;

namespace Tafs.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// An Activity line's next step, as the iPad's <c>NextStepRow</c> (<see cref="NextSteps.Action"/>): a reconnect signs in again
    /// the connection that needs it, over its Sign in again sheet (a provider's connection, which planque can't sign in again,
    /// and one this PC can't approve, open Connect apps instead), else opens Connect apps; Ask Coach opens Coach improving the
    /// taf with tafs cloud's words in its box, nothing sent.
    /// </summary>
    async Task DoNextStepAsync(HomeTaf taf, NextStep next, string fallback)
    {
        var session = App.Sessions.Kept();
        var grants = next.Kind == "reconnect" && session is not null ? await GrantsAsync(session) ?? [] : [];
        switch (NextSteps.Action(next, taf.Agent, grants, fallback))
        {
            case NextStepAction.SignInAgain { Grant: { SignsInThroughProvider: false } grant } when ConnectingFor() is { } connecting:
                var app = new ConnectApp(grant.Id) { Name = grant.VendorName, Title = grant.VendorName ?? grant.Request.Server };
                await ConnectDialog.ShowAsync(Content.XamlRoot, ((Microsoft.UI.Xaml.FrameworkElement)Content).ActualTheme, app, grant.Id, connecting, again: true);
                break;
            case NextStepAction.SignInAgain or NextStepAction.ConnectApps:
                Nav.SelectedItem = Nav.MenuItems.OfType<Microsoft.UI.Xaml.Controls.NavigationViewItem>().First(i => i.Tag is Screen.ConnectApps);
                break;
            case NextStepAction.AskCoach { Words: var words } when mayUseCoach:
                CoachMode.Improve(taf.Id, taf.Name);
                CoachMode.Write(words, newConversation: false);
                break;
        }
    }
}
