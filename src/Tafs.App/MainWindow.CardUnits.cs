using Tafs.Core.Kiff;
using Tafs.Core.NeedsYou;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// A Card unit's requests (the iPad's <c>cardUnitExceptions</c>, <c>discussCardUnit</c> and <c>dismissCardUnit</c>): Home's list on
/// Needs you, the same requests on their taf's Setup, Coach about one, and Dismiss, signed by this PC's device key.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Home's <c>card_unit_exceptions</c>, as last read.</summary>
    IReadOnlyList<CardUnitException> listedCardUnits = [];
    /// <summary>Dismissed here: gone from every list at once, until Home stops listing them.</summary>
    readonly HashSet<string> dismissedCardUnits = [];
    readonly HashSet<string> dismissingCardUnits = [];
    /// <summary>Why the last Dismiss of a request didn't go, by request.</summary>
    readonly Dictionary<string, string> cardUnitWhy = [];

    /// <summary>Home's requests taken, and drawn where they show; one Home no longer lists is forgotten.</summary>
    void TakeCardUnits(System.Text.Json.JsonElement home)
    {
        listedCardUnits = CardUnits.Read(home);
        dismissedCardUnits.IntersectWith(listedCardUnits.Select(a => a.Id));
        ShowCardUnits();
    }

    /// <summary>Signed out: the account's requests go with it.</summary>
    void ForgetCardUnits()
    {
        listedCardUnits = [];
        dismissedCardUnits.Clear();
        dismissingCardUnits.Clear();
        cardUnitWhy.Clear();
    }

    IReadOnlyList<CardUnitException> CardUnitsShown => CardUnits.Shown(listedCardUnits, dismissedCardUnits);

    void ShowCardUnits()
    {
        if (Pages.Content is NeedsYouPage page) page.ShowCardUnits(CardUnitsShown, CardUnitPanelFor);
        if (Pages.Content is TafPage tafPage) tafPage.CardUnitsChanged();
    }

    /// <summary>A request's panel, with what this person may do on it here.</summary>
    Microsoft.UI.Xaml.Controls.Border CardUnitPanelFor(CardUnitException ask) => CardUnitPanel.Build(ask,
        CardUnitTaf(ask) is { } taf && mayUseCoach && Manages(taf) ? () =>
        {
            CoachMode.Improve(taf.Id, taf.Name);
            CoachMode.Write(ask.CoachText, newConversation: false);
        } : null,
        mayAnswerHolds && CardUnitTaf(ask)?.Agent is not null ? () => _ = DismissCardUnitAsync(ask) : null,
        dismissingCardUnits.Contains(ask.Id), cardUnitWhy.GetValueOrDefault(ask.Id));

    /// <summary>The taf a request is about, by tafs cloud's id on Home.</summary>
    Tafs.Core.Home.HomeTaf? CardUnitTaf(CardUnitException ask) => home?.Tafs.FirstOrDefault(t => t.Id == ask.Taf);

    /// <summary>
    /// Dismiss, as the iPad's <c>dismissCardUnit</c> (<see cref="CardUnits.DismissAsync"/>): read again from KIFF, Windows Hello,
    /// then refused with this PC's device key. Dismissed, it leaves Needs you and the taf's Setup at once, and Home is read again.
    /// </summary>
    async Task DismissCardUnitAsync(CardUnitException ask)
    {
        var w = App.Words;
        var session = App.Sessions.Kept();
        if (session is null || CardUnitTaf(ask)?.Agent is not { } agent || !dismissingCardUnits.Add(ask.Id)) return;
        if (!CanAnswerSteps(session))
        {
            dismissingCardUnits.Remove(ask.Id);
            cardUnitWhy[ask.Id] = w.Get(TpmKey.Exists(session.KeyName) ? "needs_you.answer_in_kiff" : "needs_you.sign_in_again_to_answer");
            ShowCardUnits();
            return;
        }
        var reading = sessions;
        var bearer = session.AccessToken;
        cardUnitWhy.Remove(ask.Id);
        ShowCardUnits();
        var said = await CardUnits.DismissAsync(w, ask, agent, session.Tenant,
            async path =>
            {
                try
                {
                    var (status, body) = await KiffHttp.GetAsync(new Uri(Tafs.Core.SignIn.DeviceSignIn.Api, path), bearer, w.Language);
                    return Answered(w, status, body);
                }
                catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException) { return CloudRead.Unreached(w); }
            },
            ConfirmStepAsync,
            async message =>
            {
                using var key = TpmKey.Open(session.KeyName);
                return Convert.ToBase64String(await key.SignDerAsync(message));
            },
            async (path, body) =>
            {
                try { return await KiffHttp.PostAsync(path, body, bearer: bearer); }
                catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException) { return (0, ""); }
            },
            () => SigningTime.Next(DateTimeOffset.UtcNow), () => reading == sessions);
        if (said.Stopped || reading != sessions) return;
        dismissingCardUnits.Remove(ask.Id);
        if (said.Done) dismissedCardUnits.Add(ask.Id);
        else if (said.Why is { } why) cardUnitWhy[ask.Id] = why;
        ShowCardUnits();
        if (said.Done && cloudRead is { } read) await ReadHomeAsync(read);
    }
}
