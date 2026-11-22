using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Approvals;
using Tafs.Core.Home;
using Tafs.Core.Kiff;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// Discard, for a taf being set up, as the iPad's <c>DiscardSettingUpButton</c> and <c>discardSettingUp</c>: every word tafs
/// cloud's; the confirmation first, then <see cref="SetupDiscard.DiscardAsync"/> behind one Windows Hello; Home read again.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The agents being discarded now: their button reads Loading… and is off.</summary>
    readonly HashSet<string> discarding = [];

    /// <summary>The Cards a refused Discard named, by agent, revoked with Home's on the next one (the iPad's <c>discardCardsLeft</c>).</summary>
    readonly Dictionary<string, IReadOnlyList<string>> discardLeft = [];

    /// <summary>
    /// Discard as this session signs it: only a session that approves with this PC's passkey, as Resume (a removal is the
    /// approval key's act, never the device key's). None for any other session, and no button.
    /// </summary>
    Func<Task<DiscardResult>>? DiscardFor(SettingUp taf)
    {
        if (App.Sessions.Kept() is not { } session || !HaltResume.MayResume(session) || session.PasskeyId is not { } id) return null;
        if (taf.Agent is not { } agent || cloudRead is null) return null;
        var bearer = session.AccessToken;
        var reading = sessions;
        return () =>
        {
            var windowsHello = new NamedHello();
            var passkey = new PasskeyApprover(windowsHello, KiffSigning.FromBase64Url(id), Features.WindowsPasskeyApprovals);
            return SetupDiscard.DiscardAsync(App.Words, taf, discardLeft.GetValueOrDefault(agent) ?? [],
                async path =>
                {
                    // Windows Hello names the act in tafs cloud's own words for it (its title, else its button).
                    using var _ = HelloAct.Naming(taf.Discard is { Title.Length: > 0 } d ? d.Title : taf.Discard?.Button ?? taf.Name);
                    return (PasskeyApprover.HeaderName,
                        await passkey.ApproveAsync(session.Tenant, "POST", path, SetupDiscard.Body(), SigningTime.Next(DateTimeOffset.UtcNow)));
                },
                async path => (await KiffHttp.PostAsync(new Uri(Tafs.Core.SignIn.DeviceSignIn.Api, path), SetupDiscard.RevokeBody(), bearer,
                    App.Words.Language)).Status is >= 200 and <= 299,
                async (path, signature) =>
                {
                    try
                    {
                        var (status, body) = await KiffHttp.PostAsync(new Uri(HomeList.Api, path), SetupDiscard.Body(), bearer, App.Words.Language,
                            signature: signature);
                        return Answered(App.Words, status, body);
                    }
                    catch (System.Net.Http.HttpRequestException) { return CloudRead.Unreached(App.Words); }
                },
                () => reading == sessions && App.Sessions.Kept()?.AccessToken == bearer);
        };
    }

    /// <summary>
    /// The confirmation, in tafs cloud's words (its title, else the button; its text; yes and no), then Discard. Done, Home is
    /// read again and the row goes; not, why, in a line of its own, and the row stays.
    /// </summary>
    async Task ConfirmDiscardAsync(SettingUp taf, DiscardWords words, Func<Task<DiscardResult>> discard)
    {
        if (taf.Agent is not { } agent || discarding.Contains(agent) || Content?.XamlRoot is not { } root) return;
        var w = App.Words;
        var ask = new ContentDialog
        {
            XamlRoot = root, Title = words.Title.Length > 0 ? words.Title : words.Button,
            Content = words.Text.Length > 0 ? new TextBlock { Text = words.Text, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap } : null,
            PrimaryButtonText = words.Yes, CloseButtonText = words.No.Length > 0 ? words.No : w.Get("common.cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        try { if (await ask.ShowAsync() != ContentDialogResult.Primary) return; }
        catch (System.Runtime.InteropServices.COMException) { return; }
        var reading = sessions;
        discarding.Add(agent);
        ShowYourTafs();
        var result = await discard();
        if (reading != sessions) return;
        discarding.Remove(agent);
        if (result.CardsLeft is { } leftCards) discardLeft[agent] = leftCards;
        if (result.Done) discardLeft.Remove(agent);
        // Read again either way: done, it's gone; refused for Cards in force, Home names them.
        if (cloudRead is { } read) await ReadHomeAsync(read);
        else ShowYourTafs();
        if (result.Why is { } why && Content?.XamlRoot is { } again)
        {
            try { await new ContentDialog { XamlRoot = again, Content = why, CloseButtonText = w.Get("common.ok") }.ShowAsync(); }
            catch (System.Runtime.InteropServices.COMException) { }
        }
    }
}
