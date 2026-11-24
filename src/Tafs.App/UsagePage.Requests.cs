using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Taf;
using Tafs.Core.Usage;

namespace Tafs.App;

/// <summary>
/// How Plan &amp; usage reads and acts as the session: tafs cloud's reads (the balance, both periods' credits), KIFF's reads and
/// plain posts (its usage, asking, withdrawing), whether this person manages billing, and <see cref="Sign"/>: Raise the limit and
/// Decline, the device key's after Windows Hello (its act's words, then the request made from the tenant and a new
/// <c>signed_at</c>). No <see cref="Sign"/> on a session that can't: its page says to approve on another device.
/// </summary>
public sealed record UsageCalls(Func<string, Task<CloudRead>> Cloud, Func<string, Task<CloudRead>> Kiff, Func<string, byte[], Task<CloudRead>> KiffPost,
    Func<string, Func<string, string, DeviceSignedRequest?>, Task<DeviceSent?>>? Sign, Func<Task<bool>> ManagesBilling);

/// <summary>
/// Plan &amp; usage as the session reads it, as the iPad's <c>UsageView</c> in its order: the balance with Raise the limit (its
/// sheet, then Windows Hello and the device key), who bills the plan, the member's ask (their request waiting, with Withdraw;
/// else the last answer and Ask an admin), members' requests for someone who manages billing (a raise's Raise the limit, and
/// Decline, both signed), then the credits. KIFF's read failing is said once.
/// </summary>
public sealed partial class UsagePage
{
    /// <summary>The session's calls; none signed out or on the sample, and the page shows what it was given.</summary>
    public static UsageCalls? Calls { get; set; }

    KiffUsage? kiff;
    string? kiffWhy;
    bool managesBilling;
    bool busy;
    string? said;

    /// <summary>Everything read again: tafs cloud's balance and credits, KIFF's usage and who manages billing.</summary>
    async Task LoadAsync()
    {
        if (Calls is not { } calls) return;
        var balance = calls.Cloud("/v1/usage/balance");
        var current = calls.Cloud("/v1/usage/credits");
        var previous = calls.Cloud("/v1/usage/credits?period=previous");
        var usage = calls.Kiff(KiffUsage.Path);
        var manages = calls.ManagesBilling();
        await Task.WhenAll(balance, current, previous, usage, manages);
        if (!ReferenceEquals(Calls, calls)) return;
        // A read that failed keeps what was read before, and says why in tafs cloud's words, as the iPad's balanceError and
        // creditsError; a tafs cloud without the read (404) says nothing, as before.
        var (b0, c0, p0) = (Text(balance.Result) is { } b ? UsageScreen.ReadBalance(b) : null,
            Text(current.Result) is { } c ? UsageScreen.ReadCredits(c) : null,
            Text(previous.Result) is { } p ? UsageScreen.ReadCredits(p) : null);
        balanceWhy = b0 is null ? Why(balance.Result) : null;
        (creditsWhy, previousWhy) = (c0 is null ? Why(current.Result) : null, p0 is null ? Why(previous.Result) : null);
        reads = new(b0 ?? reads.Balance, c0 ?? reads.This, p0 ?? reads.Previous);
        kiff = usage.Result.Answer is { } u ? KiffUsage.Read(u) : null;
        kiffWhy = kiff is null ? usage.Result.Why ?? App.Words.Get("kiff.kiff_did_not_answer_expected") : null;
        managesBilling = manages.Result;
        ShowBalance(reads.Balance);
        ShowCredits();
        ShowKiff();
    }

    static string? Text(CloudRead r) => r.Answer?.GetRawText();

    /// <summary>Why a read didn't come, in tafs cloud's words; none for a tafs cloud without it (404), as the iPad's.</summary>
    static string? Why(CloudRead r) => r.Status == 404 ? null : r.Why;

    string? balanceWhy, creditsWhy, previousWhy;

    /// <summary>Who bills, the member's ask or members' requests, and Raise's line, under the balance.</summary>
    void ShowKiff()
    {
        var w = App.Words;
        KiffPanels.Children.Clear();
        // Raise the limit, for whoever manages billing where tafs cloud offers it.
        if (reads.Balance?.Raise is { } offer && managesBilling) KiffPanels.Children.Add(RaiseButton(w, offer, primary: true));
        if (said is { } why) KiffPanels.Children.Add(new TextBlock { Text = why, FontSize = 13, TextWrapping = TextWrapping.Wrap, Style = Look("TafsNoText") });
        if (kiff is not { } u)
        {
            if (Calls is not null && kiffWhy is { } failed) KiffPanels.Children.Add(Card(new TextBlock { Text = failed, FontSize = 14, TextWrapping = TextWrapping.Wrap, Style = Look("TafsNoText") }));
            return;
        }
        if (u.BillingLine(w) is { } billing) KiffPanels.Children.Add(Card(Line(billing, 15)));
        // Where to buy: a PC has no store, so the phone's app for Solo and credits, and kiff.dev for Team.
        if (reads.Balance?.WhereToBuy(u.Plan) is (true, var team))
        {
            var buy = new StackPanel { Spacing = 8 };
            buy.Children.Add(Line(w.Get("billing.buy_on_phone"), 14, soft: true));
            if (team)
            {
                var link = new HyperlinkButton { Content = w.Get("billing.team_on_web"), NavigateUri = Balance.TeamCheckout, Padding = new Thickness(0) };
                AutomationProperties.SetAutomationId(link, "usage.team-on-web");
                buy.Children.Add(link);
            }
            KiffPanels.Children.Add(Card(buy));
        }
        if (!managesBilling) AskPanel(w, u);
        else RequestsPanel(w, u);
    }

    /// <summary>A member's own request waiting, with Withdraw; else the last answer, and Ask an admin where the plan lets them ask.</summary>
    void AskPanel(Tafs.Core.Words.WordBook w, KiffUsage u)
    {
        if (u.Waiting(managesBilling: false) is { } mine)
        {
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock { Text = w.Get("usage.requests.waiting").ToUpper(new System.Globalization.CultureInfo(w.Language)), Style = Look("TafsEyebrow") });
            panel.Children.Add(Line(w.Get("usage.admin_answers", ("detail", KiffUsage.AskedLine(w, mine))), 15));
            var withdraw = new Button { Content = w.Get("usage.requests.withdraw"), Style = Look("TafsOutlineButton"), IsEnabled = !busy, HorizontalAlignment = HorizontalAlignment.Left };
            withdraw.Click += async (_, _) => await PostAsync(KiffUsage.WithdrawPath(mine.Id), "{}"u8.ToArray());
            panel.Children.Add(withdraw);
            KiffPanels.Children.Add(Card(panel));
            return;
        }
        if (u.LastAnswer(w, managesBilling: false) is { } answer) KiffPanels.Children.Add(Card(Line(answer, 14, soft: true)));
        if (u.Asks is not { } raise) return;
        var waitingBalance = reads.Balance?.State == Tone.Waiting;
        var ask = new Button { Content = w.Get("usage.requests.ask_admin"), Style = Look(waitingBalance ? "TafsPrimary" : "TafsOutlineButton"), IsEnabled = !busy,
            HorizontalAlignment = waitingBalance ? HorizontalAlignment.Stretch : HorizontalAlignment.Left, Padding = new Thickness(18, 8, 18, 8) };
        AutomationProperties.SetAutomationId(ask, "usage.ask-admin");
        ask.Click += async (_, _) => await AskAsync(w, u, raise);
        KiffPanels.Children.Add(ask);
    }

    /// <summary>Ask an admin, as the iPad's sheet: which pack (on Solo), why (optional), then Ask.</summary>
    async Task AskAsync(Tafs.Core.Words.WordBook w, KiffUsage u, bool raise)
    {
        var content = new StackPanel { Spacing = 10, MinWidth = 360 };
        RadioButtons? packs = null;
        if (!raise)
        {
            packs = new RadioButtons { Header = w.Get("usage.requests.which_pack") };
            foreach (var p in u.Askable) packs.Items.Add(new RadioButton { Content = p.Line.Length > 0 ? p.Line : p.Name, Tag = p.Id });
            packs.SelectedIndex = 0;
            content.Children.Add(packs);
        }
        var why = new TextBox { Header = w.Get("usage.requests.why_optional"), PlaceholderText = w.Get("usage.requests.why_hint"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80 };
        content.Children.Add(why);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = w.Get("usage.requests.ask_admin"), Content = content,
            PrimaryButtonText = w.Get("usage.requests.ask"), CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var pack = (packs?.SelectedItem as RadioButton)?.Tag as string;
        await PostAsync(KiffUsage.RequestsPath, KiffUsage.AskBody(raise, pack, why.Text));
    }

    /// <summary>Members' requests waiting, for someone who manages billing: each in the shared words, its reason, and Decline.</summary>
    void RequestsPanel(Tafs.Core.Words.WordBook w, KiffUsage u)
    {
        var open = u.Open(managesBilling: true);
        if (open.Count == 0) return;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = w.Get("usage.requests.title").ToUpper(new System.Globalization.CultureInfo(w.Language)), Style = Look("TafsEyebrow"),
            Foreground = ChatViews.ThemeBrush(ActualTheme, "TafsAmberText") });
        foreach (var r in open)
        {
            var item = new StackPanel { Spacing = 8 };
            item.Children.Add(Line(u.RequestLine(w, r), 15));
            if (r.Reason.Length > 0) item.Children.Add(Line(r.Reason, 14, soft: true));
            // A raise is approved by raising the limit, as the balance's Raise (raising answers every open request for one); a pack,
            // while tafs cloud sells packs, is bought in the phone's app (the iPad's Approve buys it in the store); else the shared line.
            if (r.Kind == "raise" && reads.Balance?.Raise is { } offer) item.Children.Add(RaiseButton(w, offer, primary: false));
            else item.Children.Add(Line(w.Get(r.Kind == "pack" && reads.Balance?.SellsPacks == true ? "billing.buy_on_phone" : "approval.device_can_not_approve_approve"), 13, soft: true));
            var decline = new Button { Content = w.Get(busy ? "usage.requests.declining" : "usage.requests.decline"), Style = Look("TafsOutlineButton"), IsEnabled = !busy,
                HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetAutomationId(decline, "usage.decline");
            decline.Click += async (_, _) => await DeclineAsync(w, r);
            item.Children.Add(decline);
            panel.Children.Add(item);
        }
        KiffPanels.Children.Add(new Border { Style = Look("TafsCardWarning"), Child = panel });
    }

    /// <summary>
    /// Raise the limit where it can be raised from this PC: the button that opens its sheet. Without the signer (a session that
    /// can't), the shared line saying to approve on another device; without the limit it's raised from, or its currency, the line
    /// saying it can't be raised from here.
    /// </summary>
    UIElement RaiseButton(Tafs.Core.Words.WordBook w, RaiseOffer offer, bool primary)
    {
        if (Calls?.Sign is null) return Line(w.Get("approval.device_can_not_approve_approve"), primary ? 14 : 13, soft: true);
        if (offer.Lines(w) is null) return Line(w.Get("card.limit_can_not_raised_right"), primary ? 14 : 13, soft: true);
        var raise = new Button { Content = w.Get("usage.raise_limit_button"), Style = Look(primary ? "TafsPrimary" : "TafsOutlineButton"), IsEnabled = !busy,
            HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(18, 8, 18, 8) };
        AutomationProperties.SetAutomationId(raise, primary ? "usage.raise" : "usage.request-raise");
        raise.Click += async (_, _) => await RaiseAsync(w, offer);
        return raise;
    }

    /// <summary>
    /// Raise's sheet: tafs cloud's offer (the new limit) and the limit it's raised from, read before Windows Hello; Raise the limit
    /// then asks Windows Hello and signs exactly that <c>from_cents</c>. Windows Hello closed, the sheet stays to try again. Raised
    /// or not, it closes and everything is read again: on KIFF's "the limit changed", the balance read again offers the raise
    /// from the limit it is now.
    /// </summary>
    async Task RaiseAsync(Tafs.Core.Words.WordBook w, RaiseOffer offer)
    {
        if (busy || Calls?.Sign is not { } sign || offer.Lines(w) is not var (offerLine, fromLine) || offer.FromCents is not { } from) return;
        var content = new StackPanel { Spacing = 8, MinWidth = 360 };
        content.Children.Add(Line(offerLine, 15, bold: true));
        content.Children.Add(Line(fromLine, 14, soft: true));
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = w.Get("usage.raise_limit.prompt"), Content = content,
            PrimaryButtonText = w.Get("usage.raise_limit_button"), CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
        };
        DeviceSent? sent = null;
        dialog.PrimaryButtonClick += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false;
            // The figure on the sheet is the from_cents signed: both come from this one offer.
            sent = await sign(w.Get("usage.raise_limit.prompt"), (tenant, at) => DeviceSignedRequest.Raise(tenant, from, at));
            e.Cancel = sent?.Kind == DeviceSentKind.Cancelled;
            dialog.IsPrimaryButtonEnabled = true;
            deferral.Complete();
        };
        (busy, said) = (true, null);
        ShowKiff();
        await dialog.ShowAsync();
        said = sent is null ? null : DeviceSigning.RaiseSaid(w, sent);
        busy = false;
        await LoadAsync();
    }

    /// <summary>
    /// Decline, as the iPad's sheet: a note for them (optional), then Windows Hello and this PC's signature over the request with
    /// the note as it stands at the tap (the note can't change while Windows Hello is up). Closed, the sheet stays to try again.
    /// </summary>
    async Task DeclineAsync(Tafs.Core.Words.WordBook w, PackRequest r)
    {
        if (busy || Calls is not { } calls) return;
        if (calls.Sign is not { } sign)
        {
            said = w.Get("approval.device_can_not_approve_approve");
            ShowKiff();
            return;
        }
        var note = new TextBox { Header = w.Get("usage.requests.note_optional"), PlaceholderText = w.Get("usage.requests.note_hint"), AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, MinHeight = 80, MinWidth = 360 };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = kiff?.RequestLine(w, r), Content = note,
            PrimaryButtonText = w.Get("usage.requests.decline"), CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
        };
        DeviceSent? sent = null;
        dialog.PrimaryButtonClick += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            var typed = note.Text;
            (note.IsEnabled, dialog.IsPrimaryButtonEnabled) = (false, false);
            sent = await sign(w.Get("billing.request.decline_prompt"), (tenant, at) => DeviceSignedRequest.Decline(tenant, r.Id, typed, at));
            e.Cancel = sent?.Kind == DeviceSentKind.Cancelled;
            (note.IsEnabled, dialog.IsPrimaryButtonEnabled) = (true, true);
            deferral.Complete();
        };
        (busy, said) = (true, null);
        ShowKiff();
        await dialog.ShowAsync();
        said = sent is null ? null : DeviceSigning.DeclineSaid(w, sent);
        busy = false;
        await LoadAsync();
    }

    /// <summary>A plain post to KIFF as the session (ask, withdraw), one at a time; then everything read again.</summary>
    async Task PostAsync(string path, byte[] body)
    {
        if (busy || Calls is not { } calls) return;
        (busy, said) = (true, null);
        ShowKiff();
        var sent = await calls.KiffPost(path, body);
        busy = false;
        if (sent.Answer is null) said = sent.Why ?? App.Words.Get("error.ours");
        await LoadAsync();
    }
}
