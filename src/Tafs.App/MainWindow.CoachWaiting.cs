using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.NeedsYou;

namespace Tafs.App;

/// <summary>
/// Needs you's waiting Coach chats (Home's <c>coach_waiting</c>), as the iPad's NeedsYouView: each with Open Coach, and Set aside
/// when tafs cloud sends its words; Set all aside for two or more, asked once. Setting aside is tafs cloud's "not now": nothing
/// is signed or approved.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Home's waiting chats, as tafs cloud last listed them.</summary>
    IReadOnlyList<CoachWaiting> coachWaiting = [];

    /// <summary>Home's "Set all aside", as tafs cloud sent it; none when it sent none.</summary>
    CoachWaitingClear? coachWaitingClear;

    /// <summary>The chats being set aside now: one call per chat at a time, and their rows stay away while tafs cloud answers.</summary>
    readonly HashSet<string> settingAside = [];

    /// <summary>The waiting chats Needs you draws and counts: less the one open in Coach, and those being set aside.</summary>
    IReadOnlyList<CoachWaiting> CoachWaitingShown() => CoachWaitings.Shown(coachWaiting, CoachMode.ChatOnScreen, settingAside);

    /// <summary>Home's waiting chats read again, with each Home read.</summary>
    void ReadCoachWaiting(JsonElement home)
    {
        coachWaiting = CoachWaitings.Read(home);
        coachWaitingClear = CoachWaitings.ReadClear(home);
        ShowCoachWaiting();
    }

    /// <summary>Drawn on Needs you when it shows, and counted at its sidebar row.</summary>
    void ShowCoachWaiting()
    {
        var signedIn = cloudSend is not null;
        var shown = CoachWaitingShown();
        if (Pages.Content is NeedsYouPage page)
            page.ShowCoachWaiting(shown, CoachWaitings.SetAllAside(coachWaitingClear, shown.Count, signedIn), signedIn,
                mayUseCoach: CoachMode.Answer is not null, busy: settingAside.Count > 0);
        ShowNeedsYouTotal();
    }

    /// <summary>Open Coach, from a waiting chat's card: Coach opens on that chat, where its card waits (the iPad's openCoachWaiting).</summary>
    void OpenCoachWaiting(CoachWaiting waiting)
    {
        if (CoachMode.Answer is null) return;
        CoachMode.OpenOn(waiting.Session);
        ShowCoachWaiting();
    }

    /// <summary>
    /// Sets a waiting chat aside, or every one with none (after one ask, in tafs cloud's words), as the iPad's
    /// <c>setAsideCoach</c>. The row goes once tafs cloud says so, Coach no longer shows that chat, and Home is read again for
    /// its count. A refusal brings the row back and says why, in tafs cloud's words.
    /// </summary>
    async Task SetAsideAsync(string? session)
    {
        if (cloudSend is not { } send) return;
        HashSet<string> asked = session is null ? coachWaiting.Select(c => c.Session).ToHashSet() : [session];
        if (asked.Count == 0 || asked.Overlaps(settingAside)) return;
        if (session is null && !await ConfirmSetAllAsideAsync()) return;
        var reading = sessions;
        settingAside.UnionWith(asked);
        ShowCoachWaiting();
        var answer = await send(CoachWaitings.SetAsidePath(session), "{}"u8.ToArray());
        if (reading != sessions) return;
        settingAside.ExceptWith(asked);
        if (answer.Answer is not { } done)
        {
            ShowCoachWaiting();
            (Pages.Content as NeedsYouPage)?.ShowError(answer.Why);
            return;
        }
        var ended = CoachWaitings.ReadSetAside(done).Ended.ToHashSet();
        coachWaiting = coachWaiting.Where(c => !ended.Contains(c.Session)).ToList();
        // Its next message would go on in the ended chat: a new conversation instead, as the iPad's.
        CoachMode.Leave(ended);
        (Pages.Content as NeedsYouPage)?.ShowError(null);
        ShowCoachWaiting();
        if (cloudRead is { } read) await ReadHomeAsync(read);
    }

    /// <summary>"Set all aside"'s one ask: its title names the count; its text, yes and no are tafs cloud's.</summary>
    async Task<bool> ConfirmSetAllAsideAsync()
    {
        if (coachWaitingClear is not { } words || Content?.XamlRoot is not { } root) return false;
        var ask = new ContentDialog
        {
            XamlRoot = root, Title = words.Title,
            Content = words.Text.Length > 0 ? new TextBlock { Text = words.Text, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap } : null,
            PrimaryButtonText = words.Yes, CloseButtonText = words.No.Length > 0 ? words.No : App.Words.Get("common.cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        try { return await ask.ShowAsync() == ContentDialogResult.Primary; }
        catch (System.Runtime.InteropServices.COMException) { return false; }
    }
}
