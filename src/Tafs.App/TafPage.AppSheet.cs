using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Connections;
using Tafs.Core.Taf;

namespace Tafs.App;

public sealed partial class TafPage
{
    /// <summary>Why the last Sign in again or Remove access from an app's sheet didn't go, by connection.</summary>
    readonly Dictionary<string, string> appSheetWhy = [];

    /// <summary>
    /// A connected app on Setup's ring, opened (the iPad's <c>ConnectedAppSheet</c>), titled with its name:
    /// <list type="bullet">
    /// <item>its status in tafs cloud's words, amber when it's to sign in again, with Sign in again for whoever manages the taf;</item>
    /// <item>What it may do: each action's title, its limits and its step words;</item>
    /// <item>Workflow: its words with no saved workflow, then its steps on this connection;</item>
    /// <item>the last seven days, in tafs cloud's words;</item>
    /// <item>Other connections to the same app, each with its status and Remove access;</item>
    /// <item>for whoever manages the taf, Add actions with Coach and Remove access.</item>
    /// </list>
    /// Remove access asks first in the iPad's words, then planque ends it behind Windows Hello; Setup is read again.
    /// </summary>
    async Task ShowAppSheetAsync(SetupNode node)
    {
        if (opening is not { } o) return;
        var w = App.Words;
        var app = node.App;
        var manages = o.Manages;
        var body = new StackPanel { Spacing = 14 };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = app.Name, CloseButtonText = w.Get("common.done"),
            Content = new ScrollViewer { Content = body, MaxHeight = 520 },
        };

        var status = Words(app.StatusWords, 15, app.Reconnect ? "TafsAmberWords" : "TafsSoftText");
        body.Children.Add(status);
        if (app.Reconnect && manages && o.Connecting is { } connecting)
        {
            var again = new Button { Content = w.Get("connect.sign_in_again"), Style = Look("TafsOutlineButton") };
            AutomationProperties.SetAutomationId(again, "taf.app.signInAgain." + app.Id);
            again.Click += async (_, _) =>
            {
                dialog.Hide();
                var connect = new ConnectApp(app.Id) { Name = app.Name, Title = app.Name };
                if (await ConnectDialog.ShowAsync(XamlRoot, ActualTheme, connect, app.Id, connecting, again: true)) await LoadSetupAsync();
            };
            body.Children.Add(again);
        }
        if (appSheetWhy.GetValueOrDefault(app.Id) is { } why) body.Children.Add(Words(why, 13, "TafsNoText"));

        // What it may do.
        var may = new StackPanel { Spacing = 10 };
        may.Children.Add(EyebrowText(w.Get("taf.app.what_it_may_do")));
        foreach (var action in app.Actions)
        {
            var title = Words(action.Title, 16, "TafsInkText");
            title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            var one = new StackPanel { Spacing = 5 };
            one.Children.Add(title);
            foreach (var limit in action.Limits) one.Children.Add(Words(limit, 13, "TafsSoftText"));
            if (action.StepWords.Length > 0) one.Children.Add(Words(action.StepWords, 13, "TafsDimText"));
            may.Children.Add(one);
        }
        body.Children.Add(may);

        // Workflow, on this connection.
        var flow = new StackPanel { Spacing = 8 };
        flow.Children.Add(EyebrowText(w.Get("taf.workflow.title")));
        if (app.WorkflowWords is { Length: > 0 } words) flow.Children.Add(Words(words, 14, "TafsDimText"));
        foreach (var step in app.WorkflowSteps)
        {
            var n = step.Number.ToString(System.Globalization.CultureInfo.CurrentCulture);
            flow.Children.Add(Words(step.Words.Length == 0 ? $"{n} · {step.Label}" : $"{n} · {step.Label}\n{step.Words}", 14, "TafsInkText"));
        }
        if (flow.Children.Count > 1) body.Children.Add(flow);

        // The last seven days, in tafs cloud's words.
        if (app.StatsWords.Length > 0) body.Children.Add(Words(app.StatsWords, 14, "TafsSoftText"));

        // Other sign-ins to the same app on this taf, each with its own Remove access.
        if (node.Others.Count > 0)
        {
            var others = new StackPanel { Spacing = 8 };
            others.Children.Add(EyebrowText(w.Get("connect.other_connections")));
            foreach (var other in node.Others)
            {
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(Words(other.StatusWords, 14, other.Reconnect ? "TafsAmberWords" : "TafsSoftText"));
                if (manages && o.EndConnection is not null)
                {
                    var remove = RemoveButton(w, other, dialog);
                    Grid.SetColumn(remove, 1);
                    row.Children.Add(remove);
                }
                others.Children.Add(row);
            }
            body.Children.Add(others);
        }

        if (manages)
        {
            var acts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (o.Improve is { } improve)
            {
                var add = new Button { Content = w.Get("taf.app.add_actions"), Style = Look("TafsOutlineButton") };
                add.Click += (_, _) => { dialog.Hide(); improve(); };
                acts.Children.Add(add);
            }
            if (o.EndConnection is not null) acts.Children.Add(RemoveButton(w, app, dialog));
            if (acts.Children.Count > 0) body.Children.Add(acts);
        }
        await dialog.ShowAsync();
    }

    /// <summary>
    /// Remove access to one connection, as the iPad's: "Remove access to Gmail?" and that its actions stop for this taf, then
    /// planque ends it behind Windows Hello (<see cref="TafOpening.EndConnection"/>); ended, Setup is read again.
    /// </summary>
    Button RemoveButton(Tafs.Core.Words.WordBook w, SetupConnection connection, ContentDialog sheet)
    {
        var remove = new Button { Content = w.Get("connect.disconnect_button"), Style = Look("TafsOutlineNo") };
        AutomationProperties.SetAutomationId(remove, "taf.app.remove." + connection.Id);
        remove.Click += async (_, _) =>
        {
            if (opening?.EndConnection is not { } end) return;
            sheet.Hide();
            var question = w.Get("connect.disconnect_app_question", ("app", connection.Name));
            var ask = new ContentDialog
            {
                XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = question, Content = w.Get("connect.disconnect_body"),
                PrimaryButtonText = w.Get("connect.disconnect_button"), CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
            };
            if (await ask.ShowAsync() != ContentDialogResult.Primary) return;
            var mine = opening;
            var (left, why, stopped) = await end([connection.Id], question);
            if (stopped || opening != mine) return;
            if (left == 0) appSheetWhy.Remove(connection.Id);
            else if (why is not null) appSheetWhy[connection.Id] = why;
            await LoadSetupAsync();
        };
        return remove;
    }
}
