using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.Account;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>How People &amp; groups reads KIFF as the session, and names a taf by its agent; no read for a screenshot.</summary>
public sealed record PeopleOpening(Func<string, Task<CloudRead>>? Read, Func<string, string?> TafName);

/// <summary>
/// People &amp; groups, as the iPad's <c>PeopleGroupsView</c>: the account line, then each group's name and what it uses,
/// over its people (managers first), each with their initial, their email and Manager or Member. Read when it opens and
/// every 20 seconds while open; a read that fails keeps what's shown, with why over it. It only reads: the iPad's changes
/// are signed acts.
/// </summary>
public sealed partial class PeoplePage : Page
{
    PeopleOpening? opening;
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
        Person? Find(string subject) => people.FirstOrDefault(x => x.Subject == subject);
        Groups.Children.Clear();
        foreach (var group in groups)
        {
            var header = new StackPanel { Spacing = 2, Margin = new Thickness(16, 0, 16, 0) };
            header.Children.Add(new TextBlock { Text = People.ShownName(w, group), FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Style = Look("TafsInkText") });
            header.Children.Add(new TextBlock { Text = People.Uses(w, group, opening!.TafName), FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Style = Look("TafsDimText") });
            AutomationProperties.SetHeadingLevel(header.Children[0], Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
            var rows = new StackPanel();
            var members = People.ManagersFirst(group, Find);
            for (var i = 0; i < members.Count; i++)
                rows.Children.Add(new Border { Style = Look("TafsHourLine"), Child = MemberRow(members[i], Find(members[i].Subject)),
                    Padding = new Thickness(16, 0, 16, 0), BorderThickness = new Thickness(0, i > 0 ? 1 : 0, 0, 0) });
            var section = new StackPanel { Spacing = 6, Children = { header } };
            if (members.Count > 0) section.Children.Add(new Border { Style = Look("TafsListPanel"), Child = rows });
            Groups.Children.Add(section);
        }
        NoGroups.Visibility = loaded && groups.Count == 0 && Error.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>One person in a group (the iPad's <c>memberRow</c>): their initial on a raised circle, their label, Manager in amber or Member.</summary>
    Grid MemberRow(GroupMember member, Person? person)
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
        return row;
    }
}
