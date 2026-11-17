using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Account;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// Who uses a taf KIFF runs, and sharing it (P32): its KIFF agent, KIFF read as the session, the account's email (so the
/// person reading is never named), and the change itself (<see cref="Shares.ApplyAsync"/>), only for whoever manages it on a
/// PC that approves.
/// </summary>
public sealed record TafSharing(string Agent, Func<string, Task<CloudRead>> Read, string? Email,
    Func<IReadOnlyCollection<TafShare>, IReadOnlyCollection<TafShare>, Task<(bool Done, string? Why)>>? Apply);

/// <summary>
/// Share, as the iPad's <c>ShareTafSheet</c>: step 1, who uses it, its groups (each with its people) and the account's people,
/// each chosen or not, Next once something changed; step 2, what that means for them, in the app's words, then Share, or Back.
/// Why it didn't go stays in the sheet.
/// </summary>
public sealed partial class TafPage
{
    void ShowShare()
    {
        ShareButton.Content = App.Words.Get("people.share.action");
        AutomationProperties.SetName(ShareButton, App.Words.Get("people.share.title", ("taf", NameText.Text)));
        ShareButton.Visibility = opening?.Share is { Apply: not null } ? Visibility.Visible : Visibility.Collapsed;
        _ = ReadSharedWithAsync();
    }

    /// <summary>The line under the name: "Sharing…" while KIFF is read, then who uses it; nothing for a taf KIFF doesn't run.</summary>
    async Task ReadSharedWithAsync()
    {
        if (opening?.Share is not { } sharing)
        {
            SharedWithText.Visibility = Visibility.Collapsed;
            return;
        }
        var w = App.Words;
        var shown = opening;
        SharedWithText.Text = w.Get("people.sharing");
        SharedWithText.Visibility = Visibility.Visible;
        var groupsRead = await sharing.Read(People.GroupsPath);
        var peopleRead = await sharing.Read(People.PeoplePath);
        var sharesRead = await sharing.Read(Shares.Path(sharing.Agent));
        if (!ReferenceEquals(opening, shown)) return;
        // Any read that failed says nothing rather than something untrue.
        var line = Shares.SharedWithLine(w, sharesRead.Answer is { } s ? Shares.Read(s) : null,
            groupsRead.Answer is { } g ? People.Groups(g) : null, peopleRead.Answer is { } p ? People.Members(p) : null, sharing.Email);
        SharedWithText.Text = line ?? "";
        SharedWithText.Visibility = line is null ? Visibility.Collapsed : Visibility.Visible;
    }

    async void OnShare(object sender, RoutedEventArgs e)
    {
        if (opening?.Share is not { Apply: not null } sharing) return;
        var w = App.Words;
        var taf = NameText.Text;
        ShareButton.IsEnabled = false;
        try
        {
            var groupsRead = await sharing.Read(People.GroupsPath);
            var peopleRead = await sharing.Read(People.PeoplePath);
            var sharesRead = await sharing.Read(Shares.Path(sharing.Agent));
            var groups = groupsRead.Answer is { } g ? People.Groups(g) : null;
            var people = peopleRead.Answer is { } p ? People.Members(p) : null;
            var had = sharesRead.Answer is { } s ? Shares.Read(s) : null;
            if (groups is null || people is null || had is null)
            {
                var why = new ContentDialog
                {
                    XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = w.Get("people.share.title", ("taf", taf)), CloseButtonText = w.Get("common.close"),
                    Content = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = w.Get("people.not_read",
                        ("why", groupsRead.Why ?? peopleRead.Why ?? sharesRead.Why ?? w.Get("error.ours"))) },
                };
                await why.ShowAsync();
                return;
            }
            await ShareSheetAsync(sharing, taf, groups, people, had);
        }
        finally { ShareButton.IsEnabled = true; }
        // Read again: the line under the name follows what was shared.
        await ReadSharedWithAsync();
    }

    async Task ShareSheetAsync(TafSharing sharing, string taf, IReadOnlyList<PeopleGroup> groups, IReadOnlyList<Person> people, IReadOnlyList<TafShare> had)
    {
        var w = App.Words;
        Person? Find(string subject) => people.FirstOrDefault(x => x.Subject == subject);
        var chosen = new HashSet<TafShare>(had);
        var step = 1;
        var body = new StackPanel { Spacing = 10, MinWidth = 380 };
        var why = Words("", 13, "TafsNoText");
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = w.Get("people.share.title", ("taf", taf)),
            Content = new ScrollViewer { Content = body, MaxHeight = 520 }, DefaultButton = ContentDialogButton.Primary,
        };
        bool Changed() => !chosen.SetEquals(had);

        CheckBox Pick(TafShare share, string label, string? line)
        {
            var words = new StackPanel { Spacing = 1 };
            words.Children.Add(Words(label, 15, "TafsInkText"));
            if (line is not null) words.Children.Add(Words(line, 12, "TafsDimText"));
            var box = new CheckBox { Content = words, IsChecked = chosen.Contains(share) };
            AutomationProperties.SetName(box, line is null ? label : $"{label}. {line}");
            box.Checked += (_, _) => { chosen.Add(share); dialog.IsPrimaryButtonEnabled = Changed(); };
            box.Unchecked += (_, _) => { chosen.Remove(share); dialog.IsPrimaryButtonEnabled = Changed(); };
            return box;
        }

        void Draw()
        {
            body.Children.Clear();
            why.Visibility = Visibility.Collapsed;
            if (step == 1)
            {
                body.Children.Add(Words(w.Get("people.share.step1"), 11, "TafsDimText"));
                var title = Words(w.Get("people.share.who_uses", ("taf", taf)), 20, "TafsInkText");
                title.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                body.Children.Add(title);
                if (groups.Count > 0)
                {
                    body.Children.Add(Words(w.Get("people.share.groups"), 11, "TafsDimText"));
                    foreach (var group in groups)
                        body.Children.Add(Pick(new TafShare(group.Id, null), People.ShownName(w, group), Shares.PeopleOf(w, group, Find)));
                }
                body.Children.Add(Words(w.Get("people.share.one_person"), 11, "TafsDimText"));
                foreach (var person in people)
                    body.Children.Add(Pick(new TafShare(null, person.Subject), person.Label, null));
                body.Children.Add(Words(w.Get("people.share.choose_someone"), 12, "TafsDimText"));
                (dialog.PrimaryButtonText, dialog.CloseButtonText, dialog.IsPrimaryButtonEnabled) = (w.Get("common.next"), w.Get("common.cancel"), Changed());
            }
            else
            {
                body.Children.Add(Words(w.Get("people.share.step2"), 11, "TafsDimText"));
                var title = Words(w.Get(chosen.Count == 0 ? "people.share.not_shared" : "people.share.who_gets", ("taf", taf)), 20, "TafsInkText");
                title.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                body.Children.Add(title);
                foreach (var line in Shares.Consequences(w, taf, had, chosen, groups, Find))
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    row.Children.Add(new FontIcon { Glyph = "", FontSize = 13, Style = Look("TafsOkIcon"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) });
                    var said = Words(line, 14, "TafsInkText");
                    said.MaxWidth = 440;
                    row.Children.Add(said);
                    body.Children.Add(row);
                }
                (dialog.PrimaryButtonText, dialog.CloseButtonText, dialog.IsPrimaryButtonEnabled) = (w.Get("people.share.action"), w.Get("common.back"), ApprovalGate.Ready);
            }
            body.Children.Add(why);
        }

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (step == 1) { step = 2; Draw(); return; }
            var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false;
            try
            {
                var (done, said) = await sharing.Apply!(chosen.Except(had).ToList(), had.Except(chosen).ToList());
                if (done) dialog.Hide();
                else if (said is not null) (why.Text, why.Visibility) = (said, Visibility.Visible);
            }
            finally
            {
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };
        // On step 2, the close button is Back: to step 1, with what's chosen kept.
        dialog.CloseButtonClick += (_, args) =>
        {
            if (step != 2) return;
            args.Cancel = true;
            step = 1;
            Draw();
        };
        Draw();
        // Share asks Windows Hello: while it can't confirm here, why, above the steps, and Share off.
        ApprovalGate.Notice(dialog, () => { if (step == 2) dialog.IsPrimaryButtonEnabled = ApprovalGate.Ready; });
        await dialog.ShowAsync();
    }
}
