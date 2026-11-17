using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.Account;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>How People &amp; groups reads KIFF as the session, and names a taf by its agent; no read for a screenshot.</summary>
/// <param name="Change">One change, behind Windows Hello and signed (<see cref="PeopleChanges.MakeAsync"/>); none on a PC that doesn't approve.</param>
/// <param name="ManagesPeople">KIFF's <c>members.manage</c>: groups, their names and who manages each.</param>
public sealed record PeopleOpening(Func<string, Task<CloudRead>>? Read, Func<string, string?> TafName,
    Func<PeopleChange, Task<(bool Done, string? Why)>>? Change = null, bool ManagesPeople = false);

/// <summary>
/// People &amp; groups, as the iPad's <c>PeopleGroupsView</c>: the account line, then each group's name and what it uses,
/// over its people (managers first), each with their initial, their email and Manager or Member. Read when it opens and
/// every 20 seconds while open; a read that fails keeps what's shown, with why over it. On a PC that approves, the changes
/// (P24), each behind Windows Hello and signed: "+ Group" and each group's Rename and Delete for whoever manages people;
/// "+ Add someone to Support" for whoever manages the group; each person's Make the manager or Make a member, and Remove,
/// as KIFF allows. Not for KIFF's default group, which is everyone on the account.
/// </summary>
public sealed partial class PeoplePage : Page
{
    PeopleOpening? opening;
    IReadOnlyList<PeopleGroup> shownGroups = [];
    IReadOnlyList<Person> shownPeople = [];
    bool changing;
    readonly DispatcherTimer again = new() { Interval = TimeSpan.FromSeconds(20) };
    bool loaded;

    public PeoplePage()
    {
        InitializeComponent();
        again.Tick += async (_, _) => await ReadAsync();
        Unloaded += (_, _) => again.Stop();
    }

    static Style Look(string key) => (Style)Application.Current.Resources[key];

    /// <summary>A theme brush in this page's theme, as <see cref="CoachPanel"/> finds one: Theme.xaml's dictionaries first.</summary>
    Microsoft.UI.Xaml.Media.Brush Brush(string key)
    {
        var theme = ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        foreach (var merged in Application.Current.Resources.MergedDictionaries.Prepend(Application.Current.Resources))
            if (merged.ThemeDictionaries.TryGetValue(theme, out var found) && found is ResourceDictionary d && d.TryGetValue(key, out var brush)
                && brush is Microsoft.UI.Xaml.Media.Brush b)
                return b;
        return (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[key];
    }

    /// <summary>Back to Account, where it opened from.</summary>
    void OnBack(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        BackButton.Content = "‹ " + App.Words.Get("common.back");
        var w = App.Words;
        opening = e.Parameter as PeopleOpening;
        Heading.Text = w.Get("people.title");
        NoGroups.Text = w.Get("people.no_groups");
        AddGroup.Content = w.Get("people.add_group");
        AutomationProperties.SetName(AddGroup, w.Get("people.new_group"));
        AddGroup.Visibility = opening is { Change: not null, ManagesPeople: true } ? Visibility.Visible : Visibility.Collapsed;
        await ReadAsync();
        if (opening?.Read is not null) again.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => again.Stop();

    /// <summary>The groups, the people and the account, as the iPad's <c>refreshPeople</c>: a read that works clears the error.</summary>
    async Task ReadAsync()
    {
        var w = App.Words;
        if (opening?.Read is not { } read)
        {
            Spinner.Visibility = Visibility.Collapsed;
            return;
        }
        var groupsRead = await read(People.GroupsPath);
        var peopleRead = await read(People.PeoplePath);
        var meRead = await read(Tafs.Core.Coach.CoachAccess.MePath);
        Spinner.IsActive = false;
        Spinner.Visibility = Visibility.Collapsed;
        loaded = true;
        if (groupsRead.Answer is not { } g || People.Groups(g) is not { } groups || peopleRead.Answer is not { } p || People.Members(p) is not { } people)
        {
            var why = groupsRead.Why ?? peopleRead.Why ?? w.Get("error.ours");
            Error.Text = w.Get("people.not_read", ("why", why));
            Error.Visibility = Visibility.Visible;
            NoGroups.Visibility = Visibility.Collapsed;
            return;
        }
        Error.Visibility = Visibility.Collapsed;
        if (People.Account(meRead.Answer) is { } account)
        {
            AccountLine.Text = People.AccountLine(w, account.Name, account.Email, people.Count);
            AccountLine.Visibility = Visibility.Visible;
        }
        Show(groups, people);
    }

    void Show(IReadOnlyList<PeopleGroup> groups, IReadOnlyList<Person> people)
    {
        var w = App.Words;
        (shownGroups, shownPeople) = (groups, people);
        Person? Find(string subject) => people.FirstOrDefault(x => x.Subject == subject);
        var managesPeople = opening is { Change: not null, ManagesPeople: true };
        Groups.Children.Clear();
        foreach (var group in groups)
        {
            var manages = opening?.Change is not null && PeopleChanges.Manages(group, managesPeople, Find);
            var header = new Grid { ColumnSpacing = 8, Margin = new Thickness(16, 0, 16, 0) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var said = new StackPanel { Spacing = 2 };
            said.Children.Add(new TextBlock { Text = People.ShownName(w, group), FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Style = Look("TafsInkText") });
            said.Children.Add(new TextBlock { Text = People.Uses(w, group, opening!.TafName), FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Style = Look("TafsDimText") });
            AutomationProperties.SetHeadingLevel(said.Children[0], Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
            header.Children.Add(said);
            // Rename and Delete, for whoever manages people; never KIFF's default group.
            if (managesPeople && group.Everyone != true)
            {
                var menu = new MenuFlyout();
                var rename = new MenuFlyoutItem { Text = w.Get("people.rename") };
                rename.Click += async (_, _) => await NameGroupAsync(group);
                var delete = new MenuFlyoutItem { Text = w.Get("people.delete") };
                delete.Click += async (_, _) => await DeleteGroupAsync(group);
                menu.Items.Add(rename);
                menu.Items.Add(delete);
                var more = More(menu, w.Get("people.group_actions", ("group", People.ShownName(w, group))));
                Grid.SetColumn(more, 1);
                header.Children.Add(more);
            }
            var rows = new StackPanel();
            var members = People.ManagersFirst(group, Find);
            for (var i = 0; i < members.Count; i++)
                rows.Children.Add(new Border { Style = Look("TafsHourLine"), Child = MemberRow(members[i], Find(members[i].Subject), group, managesPeople, manages),
                    Padding = new Thickness(16, 0, 16, 0), BorderThickness = new Thickness(0, i > 0 ? 1 : 0, 0, 0) });
            var section = new StackPanel { Spacing = 6, Children = { header } };
            if (members.Count > 0) section.Children.Add(new Border { Style = Look("TafsListPanel"), Child = rows });
            // Adding someone, for whoever manages the group; no one is added to everyone.
            if (manages && group.Everyone != true)
            {
                var add = new HyperlinkButton { Content = w.Get("people.add_someone", ("group", People.ShownName(w, group))), Margin = new Thickness(16, 0, 0, 0) };
                AutomationProperties.SetName(add, w.Get("people.add_someone_to", ("name", People.ShownName(w, group))));
                add.Click += async (_, _) => await AddSomeoneAsync(group, managesPeople);
                section.Children.Add(add);
            }
            Groups.Children.Add(section);
        }
        NoGroups.Visibility = loaded && groups.Count == 0 && Error.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// One person in a group (the iPad's <c>memberRow</c>): their initial on a raised circle, their label, Manager in amber or
    /// Member; then ⋯ with what may be done to them here (its context menu too): Make the manager or Make a member, for whoever
    /// manages people, and Remove from the group, as KIFF allows.
    /// </summary>
    Grid MemberRow(GroupMember member, Person? person, PeopleGroup group, bool managesPeople, bool managesGroup)
    {
        var w = App.Words;
        var label = person?.Label ?? member.Subject;
        var row = new Grid { MinHeight = 52, ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var badge = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = People.Initial(label), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Style = Look("TafsInkText") } };
        badge.Background = Brush("TafsRaised");
        AutomationProperties.SetAccessibilityView(badge, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var role = w.Get(member.IsManager ? "people.manager" : "people.member");
        var words = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = label, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis, Style = Look("TafsInkText") });
        words.Children.Add(new TextBlock { Text = role, FontSize = 12, Style = Look(member.IsManager ? "TafsAccentText" : "TafsDimText") });
        Grid.SetColumn(words, 1);
        row.Children.Add(badge);
        row.Children.Add(words);
        AutomationProperties.SetName(row, $"{label}. {role}");

        var menu = new MenuFlyout();
        if (managesPeople && opening?.Change is not null)
        {
            var other = member.IsManager ? "member" : "manager";
            var make = new MenuFlyoutItem { Text = w.Get(member.IsManager ? "people.make_member" : "people.make_manager") };
            make.Click += async (_, _) => await ChangeAsync(PeopleChanges.SetRole(w, group, member.Subject, label, other, adding: false));
            menu.Items.Add(make);
        }
        if (opening?.Change is not null && PeopleChanges.MayRemove(member, group, managesPeople, managesGroup))
        {
            var remove = new MenuFlyoutItem { Text = w.Get("people.remove_from", ("group", People.ShownName(w, group))) };
            remove.Click += async (_, _) => await ChangeAsync(PeopleChanges.Remove(w, group, member.Subject, label));
            menu.Items.Add(remove);
        }
        if (menu.Items.Count > 0)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var more = More(menu, label);
            Grid.SetColumn(more, 2);
            row.Children.Add(more);
            row.ContextFlyout = menu;
        }
        return row;
    }

    /// <summary>⋯, opening what may be done, named for whom it's for.</summary>
    Button More(MenuFlyout menu, string name)
    {
        var more = new Button { Content = new FontIcon { Glyph = "\uE712", FontSize = 14 }, Style = Look("TafsQuietButton"), Flyout = menu,
            VerticalAlignment = VerticalAlignment.Center, IsEnabled = !changing };
        AutomationProperties.SetName(more, name);
        return more;
    }

    void OnAddGroup(object sender, RoutedEventArgs e) => _ = NameGroupAsync(null);

    /// <summary>A new group's name, or a group's new one (the iPad's <c>GroupNameSheet</c>): Create or Save once there's a name.</summary>
    async Task NameGroupAsync(PeopleGroup? group)
    {
        var w = App.Words;
        var box = new TextBox { PlaceholderText = w.Get("people.group_name_hint"), Text = group?.Name ?? "", MinWidth = 320 };
        AutomationProperties.SetName(box, w.Get("people.group_name_hint"));
        var body = new StackPanel { Spacing = 10, Children = { box,
            new TextBlock { Text = w.Get(group is null ? "people.new_group_hint" : "people.rename_hint"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Style = Look("TafsDimText") } } };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Content = body, CloseButtonText = w.Get("common.cancel"),
            Title = group is null ? w.Get("people.new_group") : w.Get("people.rename_group_title", ("group", People.ShownName(w, group))),
            PrimaryButtonText = w.Get(group is null ? "people.create" : "common.save"), DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = PeopleChanges.Name(box.Text) is not null,
        };
        box.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = PeopleChanges.Name(box.Text) is not null;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || PeopleChanges.Name(box.Text) is not { } name) return;
        await ChangeAsync(group is null ? PeopleChanges.Create(w, name) : PeopleChanges.Rename(w, group, name));
    }

    /// <summary>Delete, after its question and what deleting does (the iPad's confirmation).</summary>
    async Task DeleteGroupAsync(PeopleGroup group)
    {
        var w = App.Words;
        var shown = People.ShownName(w, group);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = w.Get("people.delete_group_title", ("group", shown)),
            Content = new TextBlock { Text = w.Get("people.delete_group_body"), TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = w.Get("people.delete_group_button", ("group", shown)), CloseButtonText = w.Get("common.cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ChangeAsync(PeopleChanges.Delete(w, group));
    }

    /// <summary>
    /// Adding one of the account's people (the iPad's <c>AddToGroupSheet</c>): As the group's manager first, for whoever manages
    /// people, then everyone not in it yet, each a tap; or that everyone is here already.
    /// </summary>
    async Task AddSomeoneAsync(PeopleGroup group, bool managesPeople)
    {
        var w = App.Words;
        var body = new StackPanel { Spacing = 8 };
        var asManager = new ToggleSwitch { Header = w.Get("people.as_manager") };
        if (managesPeople)
        {
            body.Children.Add(asManager);
            body.Children.Add(new TextBlock { Text = w.Get("people.manager_explains"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Style = Look("TafsDimText") });
        }
        body.Children.Add(new TextBlock { Text = w.Get("people.account_people_title"), FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Style = Look("TafsDimText"), Margin = new Thickness(0, 6, 0, 0) });
        var outside = PeopleChanges.Outside(group, shownPeople);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = w.Get("people.add_to", ("group", People.ShownName(w, group))),
            Content = new ScrollViewer { Content = body, MaxHeight = 480 }, CloseButtonText = w.Get("common.cancel"),
        };
        PeopleChange? chosen = null;
        foreach (var person in outside)
        {
            var pick = new Button { Content = person.Label, Style = Look("TafsQuietButton"), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left };
            pick.Click += (_, _) =>
            {
                chosen = PeopleChanges.SetRole(w, group, person.Subject, person.Label, managesPeople && asManager.IsOn ? "manager" : "member", adding: true);
                dialog.Hide();
            };
            body.Children.Add(pick);
        }
        if (outside.Count == 0) body.Children.Add(new TextBlock { Text = w.Get("people.everyone_in"), FontSize = 14, TextWrapping = TextWrapping.Wrap, Style = Look("TafsDimText") });
        await dialog.ShowAsync();
        if (chosen is { } change) await ChangeAsync(change);
    }

    /// <summary>One change at a time, behind Windows Hello and signed; then read again, with why it didn't go through over the list.</summary>
    async Task ChangeAsync(PeopleChange change)
    {
        if (changing || opening?.Change is not { } make) return;
        changing = true;
        ChangeError.Visibility = Visibility.Collapsed;
        (bool Done, string? Why) result;
        try { result = await make(change); }
        finally { changing = false; }
        await ReadAsync();
        if (result.Why is { } why) (ChangeError.Text, ChangeError.Visibility) = (why, Visibility.Visible);
    }
}
