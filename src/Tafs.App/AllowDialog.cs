using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tafs.Core.NeedsYou;
using Tafs.Core.Words;

namespace Tafs.App;

/// <summary>
/// What the sheet does, given by the window: tafs cloud's offer then KIFF's draft (<see cref="Load"/>, with round), and the
/// confirm (<see cref="Confirm"/>). Each says when another session began meanwhile (<c>Stopped</c>): the sheet then closes
/// and shows nothing of it. <see cref="Approves"/> is a session that signs with this PC's passkey; any other keeps the gate.
/// </summary>
sealed record AllowCalls(Func<bool, Task<(AllowRead? Read, string? Why, bool Stopped)>> Load,
    Func<AllowSheet, byte[], Task<(bool Done, string? Why, bool Stopped)>> Confirm, bool Approves, Action AddThisDevice);

/// <summary>
/// "Allow this from now on", as the iPad's <c>AllowSheetView</c>: tafs cloud's change, before and after, in its words; KIFF's
/// own lines for each Card that changes, under them; what it would have done; the held call's switch; and one Windows Hello,
/// only when KIFF's draft is what was offered. A <c>none</c> says tafs cloud's words, with nothing to sign.
/// </summary>
sealed class AllowDialog
{
    readonly AllowCalls calls;
    readonly ElementTheme theme;
    readonly StackPanel body = new() { Spacing = 12 };
    ContentDialog? dialog;
    AllowSheet sheet;

    AllowDialog(HeldCall call, AllowCalls calls, ElementTheme theme)
    {
        sheet = AllowSheet.Open(call);
        this.calls = calls;
        this.theme = theme;
    }

    /// <summary>The sheet open now, as the iPad's one <c>allow</c>: a second tap opens none, and a sign-out closes it.</summary>
    static ContentDialog? open;

    /// <summary>Closes the sheet, as a sign-out empties the iPad's held calls and its sheet with them.</summary>
    public static void Close() => open?.Hide();

    public static async Task ShowAsync(XamlRoot root, ElementTheme theme, HeldCall call, AllowCalls calls)
    {
        if (open is not null) return;
        var allow = new AllowDialog(call, calls, theme);
        allow.dialog = new ContentDialog
        {
            XamlRoot = root, RequestedTheme = theme, CloseButtonText = App.Words.Get("common.cancel"),
            Content = new ScrollViewer { Content = allow.body, MaxHeight = 640 },
        };
        allow.Draw();
        open = allow.dialog;
        try
        {
            _ = allow.LoadAsync(round: false);
            await allow.dialog.ShowAsync();
        }
        finally { open = null; }
    }

    /// <summary>tafs cloud's offer now, then KIFF's draft, as the iPad's <c>loadAllow</c>; rounding up keeps the switch.</summary>
    async Task LoadAsync(bool round)
    {
        var (read, why, stopped) = await calls.Load(round);
        if (stopped) { dialog?.Hide(); return; }
        sheet = read is { } r ? sheet.Loaded(r, round) : sheet.Failed(App.Words, why);
        Draw();
    }

    /// <summary>
    /// Round up to the figure the offer named, as the iPad's <c>roundAllow</c>: tafs cloud makes the rounded draft, which is
    /// shown and checked the same way. The app never rounds.
    /// </summary>
    void Round()
    {
        if (sheet.Busy) return;
        sheet = sheet with { Busy = true, Error = null };
        Draw();
        _ = LoadAsync(round: true);
    }

    /// <summary>The one signature, as the iPad's <c>confirmAllow</c>: taken, the sheet closes and Needs you reads again.</summary>
    async Task ConfirmAsync()
    {
        var (bodyBytes, why) = sheet.Confirmation(App.Words);
        if (bodyBytes is null)
        {
            if (why is not null) { sheet = sheet with { Error = why }; Draw(); }
            return;
        }
        sheet = sheet with { Busy = true, Error = null };
        Draw();
        (bool Done, string? Why, bool Stopped) came;
        // Whatever goes wrong, the sheet never stays waiting: it says why, and nothing more is sent.
        try { came = await calls.Confirm(sheet, bodyBytes); }
        catch (Exception e) { came = (false, SessionStore.Why(e), false); }
        var (done, refused, stopped) = came;
        if (stopped || done) { dialog?.Hide(); return; }
        // Nothing was sent: why shows on the sheet ("Cancelled. Nothing changed." when Windows Hello was closed).
        sheet = refused is null ? sheet with { Busy = false } : sheet.Failed(App.Words, refused);
        Draw();
    }

    void Draw()
    {
        var w = App.Words;
        body.Children.Clear();
        var title = Words(w.Get("needs_you.allow_title"), 22, "TafsInk", FontWeights.Bold);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHeadingLevel(title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1);
        body.Children.Add(title);
        if (sheet.Answer is { } answer)
        {
            if (answer.Offer is { } offer) Offered(w, offer);
            // No offer, whatever its reason, one this app knows or not: tafs cloud's words, nothing to sign.
            else body.Children.Add(Words(answer.Words ?? "", 15, "TafsSoft"));
        }
        else if (sheet.Busy) body.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center });
        if (sheet.Error is { } error) body.Children.Add(Words(error, 13, "TafsNo"));
    }

    void Offered(WordBook w, AllowOffer offer)
    {
        foreach (var change in offer.Changes)
        {
            var pair = new StackPanel { Spacing = 2 };
            pair.Children.Add(Words(change.WordsBefore ?? "", 14, "TafsSoft"));
            pair.Children.Add(Words("→ " + (change.WordsAfter ?? ""), 16, "TafsInk", FontWeights.SemiBold));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(pair, $"{change.WordsBefore} → {change.WordsAfter}");
            body.Children.Add(pair);
        }
        // KIFF's own lines for each Card that changes, as it keeps them: what's signed.
        foreach (var line in sheet.KiffLines(w)) body.Children.Add(Words(line, 13, "TafsDim"));
        if (offer.Would?.Words is { Length: > 0 } would) body.Children.Add(Words(would, 14, "TafsInk"));
        if (!sheet.Matches)
        {
            if (sheet.NotOffered) body.Children.Add(Words(w.Get("needs_you.allow_not_offered"), 14, "TafsNo"));
            return;
        }
        var send = new ToggleSwitch { Header = w.Get("needs_you.allow_send_this_too"), IsOn = sheet.SendHeld, IsEnabled = !sheet.Busy };
        send.Toggled += (_, _) => { if (send.IsOn != sheet.SendHeld) { sheet = sheet with { SendHeld = send.IsOn }; Draw(); } };
        body.Children.Add(send);
        if (!sheet.SendHeld) body.Children.Add(Words(w.Get("needs_you.allow_switch_off"), 13, "TafsSoft"));
        if (sheet.RoundTo is { } figure)
        {
            var round = Button(w.Get("needs_you.allow_round_up", ("amount", figure)), "TafsOutlineButton");
            round.Click += (_, _) => Round();
            body.Children.Add(round);
        }
        if (!calls.Approves)
        {
            // The iPad's ApprovalGate: why this PC can't approve, and the way to add it.
            var gate = NeedsYouScreen.Gate(w);
            body.Children.Add(Words(gate.Lead, 13, "TafsSoft"));
            var add = Button(gate.Add, "TafsOutlineButton");
            add.Click += (_, _) => { dialog?.Hide(); calls.AddThisDevice(); };
            body.Children.Add(add);
            return;
        }
        var confirm = Button(sheet.Busy ? w.Get("approval.approving") : w.Get("approval.approve_with", ("biometry", "Windows Hello")),
            sheet.Busy ? "TafsPrimaryOff" : "TafsPrimary");
        confirm.Click += (_, _) => _ = ConfirmAsync();
        // Windows Hello not ready here: why and what to do, in Approve's place.
        body.Children.Add(ApprovalGate.Wrap(confirm));
    }

    Button Button(string text, string style)
    {
        var b = new Button
        {
            Content = text, IsEnabled = !sheet.Busy, HorizontalAlignment = HorizontalAlignment.Left,
            Style = (Style)Application.Current.Resources[style],
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, text);
        return b;
    }

    TextBlock Words(string text, double size, string colour, Windows.UI.Text.FontWeight? weight = null) => new()
    {
        Text = text, FontSize = size, Foreground = Brush(colour), TextWrapping = TextWrapping.Wrap, FontWeight = weight ?? FontWeights.Normal,
        IsTextSelectionEnabled = true,
    };

    /// <summary>A theme brush for the sheet's theme, as the app's code takes them.</summary>
    Brush Brush(string key)
    {
        var name = theme == ElementTheme.Dark ? "Dark" : "Light";
        foreach (var merged in Application.Current.Resources.MergedDictionaries.Prepend(Application.Current.Resources))
            if (merged.ThemeDictionaries.TryGetValue(name, out var found) && found is ResourceDictionary d && d.TryGetValue(key, out var brush) && brush is Brush b)
                return b;
        return (Brush)Application.Current.Resources[key];
    }
}
