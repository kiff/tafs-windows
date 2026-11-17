using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Coach;

namespace Tafs.App;

/// <summary>
/// Coach's tools still to give (P8), as the iPad's <c>CoachToolsRow</c> and <c>CoachToolsSheet</c>: one line over the box while
/// tafs cloud says they're to give, opening the step in tafs cloud's words (its title and intro, its groups, each tool and Coach's
/// Cards in KIFF's words under Details), then Give, under one Windows Hello, or Not now. Nothing once they're given.
/// </summary>
public sealed partial class CoachPanel
{
    /// <summary>Giving them (<see cref="CoachToolsGive.GiveAsync"/>), as this session.</summary>
    public Func<Task<ToolsGiven>>? GiveTools { get; set; }

    /// <summary>Coach's tools as tafs cloud said with the last read of <c>/v1/setup</c>, which each conversation read makes.</summary>
    CoachTools? tools;

    /// <summary>What <c>/v1/setup</c> said of Coach's tools, read with the conversation.</summary>
    void ShowTools(System.Text.Json.JsonElement? setup)
    {
        if (setup is { } s) tools = CoachTools.Read(s);
        ShowToolsRow();
    }

    void ShowToolsRow()
    {
        var row = GiveTools is null ? null : tools?.Row(App.Words);
        ToolsRow.Content = row ?? "";
        ToolsRow.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;
    }

    async void OnToolsRow(object sender, RoutedEventArgs e)
    {
        if (tools is not { ToGive: true } shown || GiveTools is not { } give) return;
        var w = App.Words;
        var step = shown.Step;
        var body = new StackPanel { Spacing = 10 };
        if (step?.Intro is { Length: > 0 } intro) body.Children.Add(Words(intro, 15, "TafsInkText"));
        foreach (var g in step?.Groups ?? [])
        {
            if (g.Name.Length > 0) body.Children.Add(Words(g.Name, 14, "TafsInkText"));
            if (g.Line.Length > 0) body.Children.Add(Words(g.Line, 13, "TafsSoftText"));
        }
        // Each tool and its limits, and Coach's Cards in KIFF's words, folded.
        var details = new StackPanel { Spacing = 6 };
        foreach (var g in step?.Groups ?? [])
            foreach (var t in g.Tools)
            {
                details.Children.Add(Words(t.Name, 13, "TafsInkText"));
                if (t.Does.Length > 0) details.Children.Add(Words(t.Does, 13, "TafsSoftText"));
                if (t.Why.Length > 0) details.Children.Add(Words(t.Why, 12, "TafsDimText"));
            }
        if (shown.Card is { } card)
        {
            if (card.CardTitle is { Length: > 0 } title) details.Children.Add(Words(title, 13, "TafsInkText"));
            foreach (var c in card.Cards)
            {
                if (c.Title.Length > 0) details.Children.Add(Words(c.Title, 13, "TafsInkText"));
                foreach (var line in c.Lines) details.Children.Add(Words(line, 12, "TafsSoftText"));
            }
        }
        if (details.Children.Count > 0)
            body.Children.Add(new Expander { Header = step?.Details is { Length: > 0 } d ? d : w.Get("common.details"), Content = details,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left });
        var why = Words("", 13, "TafsNoText");
        why.Visibility = Visibility.Collapsed;
        body.Children.Add(why);
        var giveWords = step?.Give is { Length: > 0 } gw ? gw : w.Get("coach.tools.give");
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = step?.Title is { Length: > 0 } t0 ? t0 : w.Get("coach.tools.title"),
            Content = new ScrollViewer { Content = body, MaxHeight = 520 }, PrimaryButtonText = giveWords,
            CloseButtonText = step?.NotNow is { Length: > 0 } nn ? nn : w.Get("common.not_now"), DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            (dialog.PrimaryButtonText, dialog.IsPrimaryButtonEnabled, why.Visibility) = (w.Get("coach.tools.giving"), false, Visibility.Collapsed);
            try
            {
                var given = await give();
                if (given.Given) dialog.Hide();
                else if (given.Why is { } said) (why.Text, why.Visibility) = (said, Visibility.Visible);
            }
            finally
            {
                (dialog.PrimaryButtonText, dialog.IsPrimaryButtonEnabled) = (giveWords, true);
                deferral.Complete();
            }
        };
        await dialog.ShowAsync();
        // Read again: the row goes once they're given.
        await LoadThreadAsync();
    }
}
