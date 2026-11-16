using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Taf;
using Tafs.Core.Usage;

namespace Tafs.App;

/// <summary>
/// How Plan &amp; usage reads and acts as the session: tafs cloud's reads (the balance, both periods' credits), KIFF's reads and
/// plain posts (its usage, asking, withdrawing), the signed decline, and whether this person manages billing.
/// </summary>
public sealed record UsageCalls(Func<string, Task<CloudRead>> Cloud, Func<string, Task<CloudRead>> Kiff, Func<string, byte[], Task<CloudRead>> KiffPost,
    Func<PackRequest, string, Task<string?>> Decline, Func<Task<bool>> ManagesBilling);

/// <summary>
/// Plan &amp; usage as the session reads it, as the iPad's <c>UsageView</c> in its order: the balance (Raise says "Approve on your
/// phone" where its button would be: nothing that moves money happens on Windows in this release), who bills the plan, the
/// member's ask (their request waiting, with Withdraw; else the last answer and Ask an admin), members' requests for someone
/// who manages billing (Decline only, signed), then the credits. KIFF's read failing is said once.
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
        reads = new(Text(balance.Result) is { } b ? UsageScreen.ReadBalance(b) : null,
            Text(current.Result) is { } c ? UsageScreen.ReadCredits(c) : null,
            Text(previous.Result) is { } p ? UsageScreen.ReadCredits(p) : null);
        kiff = usage.Result.Answer is { } u ? KiffUsage.Read(u) : null;
        kiffWhy = kiff is null ? usage.Result.Why ?? App.Words.Get("kiff.kiff_did_not_answer_expected") : null;
        managesBilling = manages.Result;
        ShowBalance(reads.Balance);
        ShowCredits();
        ShowKiff();
    }

    static string? Text(CloudRead r) => r.Answer?.GetRawText();

    /// <summary>Who bills, the member's ask or members' requests, and Raise's line, under the balance.</summary>
    void ShowKiff()
    {
        var w = App.Words;
        KiffPanels.Children.Clear();
        // Raise the limit moves money: on Windows, the shared line where its button would be.
        if (reads.Balance?.Raise is not null && managesBilling) KiffPanels.Children.Add(Line(w.Get("approval.device_can_not_approve_approve"), 14, soft: true));
        if (said is { } why) KiffPanels.Children.Add(new TextBlock { Text = why, FontSize = 13, TextWrapping = TextWrapping.Wrap, Style = Look("TafsNoText") });
        if (kiff is not { } u)
        {
            if (Calls is not null && kiffWhy is { } failed) KiffPanels.Children.Add(Card(new TextBlock { Text = failed, FontSize = 14, TextWrapping = TextWrapping.Wrap, Style = Look("TafsNoText") }));
            return;
        }
        if (u.BillingLine(w) is { } billing) KiffPanels.Children.Add(Card(Line(billing, 15)));
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
            // Approving moves money (a raise, or buying the pack): on Windows, the shared line where its button would be.
            item.Children.Add(Line(w.Get("approval.device_can_not_approve_approve"), 13, soft: true));
            var decline = new Button { Content = w.Get(busy ? "usage.requests.declining" : "usage.requests.decline"), Style = Look("TafsOutlineButton"), IsEnabled = !busy,
                HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetAutomationId(decline, "usage.decline");
            decline.Click += async (_, _) => await DeclineAsync(w, r);
            item.Children.Add(decline);
            panel.Children.Add(item);
        }
        KiffPanels.Children.Add(new Border { Style = Look("TafsCardWarning"), Child = panel });
    }

    /// <summary>Decline, as the iPad's sheet: a note for them (optional), then Windows Hello and this PC's signature.</summary>
    async Task DeclineAsync(Tafs.Core.Words.WordBook w, PackRequest r)
    {
        if (Calls is not { } calls) return;
        var note = new TextBox { Header = w.Get("usage.requests.note_optional"), PlaceholderText = w.Get("usage.requests.note_hint"), AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, MinHeight = 80, MinWidth = 360 };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = kiff?.RequestLine(w, r), Content = note,
            PrimaryButtonText = w.Get("usage.requests.decline"), CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        (busy, said) = (true, null);
        ShowKiff();
        said = await calls.Decline(r, note.Text);
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
