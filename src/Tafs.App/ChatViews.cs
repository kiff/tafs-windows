using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tafs.Core.Coach;

namespace Tafs.App;

/// <summary>
/// A chat's pieces, drawn the same in Coach's thread and a taf's, as the iPad's <c>ChatBubble</c>, <c>WorkSummary</c>,
/// <c>RunLine</c>, <c>RefusedLines</c> and <c>AnsweredCardLine</c> are shared by <c>SetupChatView</c> and <c>TafChatView</c>.
/// Colours come from the view drawing them (<paramref name="brush"/>), in its own theme.
/// </summary>
static class ChatViews
{
    /// <summary>
    /// A theme brush in a view's theme: Theme.xaml's dictionaries are merged into the app's, so they're looked for there; a
    /// brush that's the same in both themes (the brand's gradient) is the app's own.
    /// </summary>
    public static Brush ThemeBrush(ElementTheme actual, string key)
    {
        var theme = actual == ElementTheme.Dark ? "Dark" : "Light";
        foreach (var merged in Application.Current.Resources.MergedDictionaries.Prepend(Application.Current.Resources))
            if (merged.ThemeDictionaries.TryGetValue(theme, out var found) && found is ResourceDictionary d && d.TryGetValue(key, out var brush)
                && brush is Brush b)
                return b;
        return (Brush)Application.Current.Resources[key];
    }

    /// <summary>The person's words, on the right in a brand bubble, as the iPad's owner <c>ChatBubble</c>.</summary>
    public static Border OwnerBubble(string words, Func<string, Brush> brush) => new()
    {
        Background = brush("TafsBrand"), CornerRadius = new CornerRadius(18),
        Padding = new Thickness(14, 10, 14, 10), HorizontalAlignment = HorizontalAlignment.Right,
        Child = new TextBlock { Text = words, FontSize = 15, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) },
    };

    /// <summary>
    /// A message that wasn't taken, kept where it was written, as the iPad's <c>UnsentBubble</c>: the person's bubble faded,
    /// "Not sent · why" in red under it, then Edit (its words back in the box) and Retry (sent again once). Copy and Delete
    /// in its menu. Narrator reads it as one line, with Retry and Edit as its actions' buttons.
    /// </summary>
    public static StackPanel UnsentBubble(Tafs.Core.Words.WordBook w, Tafs.Core.Taf.UnsentMessage m, Func<string, Brush> brush, bool busy,
        Action retry, Action edit, Action delete, Action? resume = null)
    {
        var box = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(48, 0, 0, 0) };
        var bubble = OwnerBubble(m.Text, brush);
        bubble.Opacity = 0.6;
        box.Children.Add(bubble);
        var why = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        // A halt's refusal has the iPad's raised hand; any other, its error mark.
        why.Children.Add(new FontIcon { Glyph = resume is null ? "\uEA39" : "\uE768", FontSize = 13, Foreground = brush("TafsNo"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
        why.Children.Add(new TextBlock { Text = Tafs.Core.Taf.Unsent.Line(w, m), FontSize = 13, Foreground = brush("TafsNo"), TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Right, MaxWidth = 520 });
        box.Children.Add(why);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Right };
        var editButton = new HyperlinkButton { Content = w.Get("setup.edit"), FontSize = 14, Padding = new Thickness(0), Foreground = brush("TafsSoft") };
        editButton.Click += (_, _) => edit();
        var retryButton = new HyperlinkButton { Content = w.Get("chat.retry"), FontSize = 14, Padding = new Thickness(0), IsEnabled = !busy,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = brush("TafsInk") };
        retryButton.Click += (_, _) => retry();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(retryButton, "unsent.retry");
        if (resume is not null)
        {
            // Retry can't pass a halt: Resume opens the Halt screen, where resuming is the person's act behind Windows Hello.
            var resumeButton = new HyperlinkButton { Content = w.Get("taf.resume"), FontSize = 14, Padding = new Thickness(0),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = brush("TafsInk") };
            resumeButton.Click += (_, _) => resume();
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(resumeButton, "unsent.resume");
            retryButton.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            retryButton.Foreground = brush("TafsSoft");
            actions.Children.Add(resumeButton);
        }
        else actions.Children.Add(editButton);
        actions.Children.Add(retryButton);
        box.Children.Add(actions);
        var menu = new MenuFlyout();
        var copy = new MenuFlyoutItem { Text = w.Get("common.copy") };
        copy.Click += (_, _) =>
        {
            var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
            data.SetText(m.Text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
        };
        var drop = new MenuFlyoutItem { Text = w.Get("chat.delete") };
        drop.Click += (_, _) => delete();
        menu.Items.Add(copy);
        menu.Items.Add(drop);
        box.ContextFlyout = menu;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(bubble, $"{m.Text}. {Tafs.Core.Taf.Unsent.Line(w, m)}");
        return box;
    }

    /// <summary>A halted refusal's Resume: the window opens Halt's screen, for the taf by tafs cloud's id, or none for the account.</summary>
    public static event Action<string?, string>? ResumeChosen;

    public static void ChooseResume(string? taf, string name) => ResumeChosen?.Invoke(taf, name);

    /// <summary>
    /// A door's stamp, as the iPad's <c>DoorStamp</c>: mono 10 medium, two lines at most, blue, red when it wasn't sent back;
    /// over the person's side of the thread on the right, under the taf's answer on the left. Narrator reads it in lower case.
    /// </summary>
    public static TextBlock DoorStamp(ChatDoor door, bool fromOwner, Func<string, Brush> brush)
    {
        var text = door.Stamp(App.Words, fromOwner);
        var stamp = new TextBlock
        {
            Text = text, FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.Medium, FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            MaxLines = 2, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = brush(door.NotSent ? "TafsNo" : "TafsBlue"),
            HorizontalAlignment = fromOwner ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            TextAlignment = fromOwner ? TextAlignment.Right : TextAlignment.Left,
        };
        AutomationProperties.SetName(stamp, text.ToLowerInvariant());
        return stamp;
    }

    /// <summary>
    /// The files sent with a message, as the iPad's <c>MessageFiles</c>: each its own row on the right, its kind's mark and its
    /// name in 14 medium, one line, on the panel colour with a line round it, corners of 12. Opening one full size comes with
    /// reading its bytes; until then each is its name.
    /// </summary>
    public static StackPanel MessageFiles(IReadOnlyList<ChatFile> files, Func<string, Brush> brush)
    {
        var column = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var file in files)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            // An image's mark, else a document's, as the iPad's FileIcon by media type.
            row.Children.Add(new FontIcon { Glyph = file.MediaType.StartsWith("image/", StringComparison.Ordinal) ? "\uEB9F" : "\uE8A5", FontSize = 16,
                Foreground = brush("TafsSoft") });
            row.Children.Add(new TextBlock { Text = file.Name, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis, Foreground = brush("TafsInk"), VerticalAlignment = VerticalAlignment.Center });
            var chip = new Border
            {
                Child = row, Padding = new Thickness(6, 6, 12, 6), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
                Background = brush("TafsPanel"), BorderBrush = brush("TafsLine"), HorizontalAlignment = HorizontalAlignment.Right,
            };
            AutomationProperties.SetName(chip, file.Name);
            column.Children.Add(chip);
        }
        return column;
    }

    /// <summary>Under an answer written while a card waited, as the iPad's <c>AnsweredCardLine</c>: "Answered the card above".</summary>
    public static TextBlock AnsweredCardLine() =>
        CoachPanel.Words(App.Words.Get("chat.answered_card_above"), 12, "TafsDimText", HorizontalAlignment.Right);

    /// <summary>A run's start, as the iPad's kickoff line: "Run started 09:00 · …", centred, two lines at most.</summary>
    public static TextBlock Kickoff(ChatMessage m)
    {
        var line = CoachPanel.Words(App.Words.Get("chat.run_started_with", ("time", TimeZoneInfo.ConvertTime(m.At, TimeZoneInfo.Local).ToString("HH:mm",
            System.Globalization.CultureInfo.InvariantCulture)), ("text", m.Text)), 12, "TafsDimText", HorizontalAlignment.Center);
        line.MaxLines = 2;
        line.TextTrimming = TextTrimming.CharacterEllipsis;
        return line;
    }

    /// <summary>One of tafs cloud's lines about a run, as the iPad's <c>RunLine</c>: small and secondary, drawn as written.</summary>
    public static TextBlock RunLine(string text)
    {
        var line = CoachPanel.Words(text, 13, "TafsSoftText");
        line.IsTextSelectionEnabled = true;
        return line;
    }

    /// <summary>
    /// A refused connection's lines, as the iPad's <c>RefusedLines</c>: the step's in the failure colour, then why and what to
    /// do next, each as tafs cloud wrote it, never folded.
    /// </summary>
    public static StackPanel RefusedLines(IReadOnlyList<string> refused, Func<string, Brush> brush)
    {
        var lines = new StackPanel { Spacing = 2 };
        for (var i = 0; i < refused.Count; i++)
        {
            var line = CoachPanel.Words(refused[i], 13, "TafsSoftText");
            line.IsTextSelectionEnabled = true;
            if (i == 0)
            {
                line.Foreground = brush("TafsNo");
                line.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
            }
            lines.Children.Add(line);
        }
        AutomationProperties.SetName(lines, string.Join(" ", refused));
        return lines;
    }

    /// <summary>
    /// A finished answer's work, folded, as the iPad's <c>WorkSummary</c>: "Worked for 12 s ›", which opens its steps, each with
    /// its mark, "2 tries" after a step tried again.
    /// </summary>
    public static StackPanel WorkSummary(IReadOnlyList<WorkRow> rows, int ms, Func<string, Brush> brush)
    {
        var w = App.Words;
        var took = WorkLog.Duration(w, ms);
        var chevron = new FontIcon { Glyph = "", FontSize = 11, Foreground = brush("TafsDim") };
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        label.Children.Add(CoachPanel.Words(w.Get("chat.work.worked_for", ("ms", took)), 13, "TafsDimText"));
        label.Children.Add(chevron);
        var toggle = new Button { Content = label, Style = (Style)Application.Current.Resources["TafsQuietButton"], Padding = new Thickness(0) };
        AutomationProperties.SetName(toggle, w.Count("chat.work.worked_for_steps", rows.Count, ("took", took)));
        AutomationProperties.SetHelpText(toggle, w.Get("chat.work.shows_steps"));
        var steps = new StackPanel { Spacing = 4, Padding = new Thickness(6, 0, 0, 0), Margin = new Thickness(4, 0, 0, 0), Visibility = Visibility.Collapsed,
            BorderThickness = new Thickness(1, 0, 0, 0), BorderBrush = brush("TafsLine") };
        foreach (var row in rows)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            line.Children.Add(new FontIcon { Glyph = row.Status switch { StepStatus.Error => "", StepStatus.NotMade => "", _ => "" }, FontSize = 11, Width = 14,
                Foreground = brush(row.Status == StepStatus.Error ? "TafsNo" : "TafsDim") });
            line.Children.Add(CoachPanel.Words(WorkLog.Past(row.Words), 13, "TafsSoftText"));
            if (row.Status == StepStatus.NotMade) line.Children.Add(CoachPanel.Words(w.Get("chat.work.not_made_read_first"), 12, "TafsDimText"));
            else if (row.Tries > 1) line.Children.Add(CoachPanel.Words(w.Count("chat.work.tries", row.Tries), 12, "TafsDimText"));
            steps.Children.Add(line);
        }
        toggle.Click += (_, _) =>
        {
            var open = steps.Visibility != Visibility.Visible;
            steps.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            chevron.Rotation = open ? 90 : 0;
            AutomationProperties.SetHelpText(toggle, w.Get(open ? "chat.work.hides_steps" : "chat.work.shows_steps"));
        };
        chevron.CenterPoint = new System.Numerics.Vector3(5.5f, 5.5f, 0);
        var summary = new StackPanel { Spacing = 6 };
        summary.Children.Add(toggle);
        summary.Children.Add(steps);
        return summary;
    }
}
