using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Coach;

namespace Tafs.App;

/// <summary>
/// "Taking shape" (P31), as the iPad's <c>SetupShapePanel</c> (kiff/tafs#85): beside Coach's conversation on a wide panel,
/// what it has made so far, read only from what this PC holds (<see cref="CoachShape"/>): the taf's name, the plan, the
/// tools it connects and the Card KIFF drafted. The cards to answer stay in the conversation.
/// </summary>
public sealed partial class CoachPanel
{
    /// <summary>
    /// Whether it shows, as the iPad's <c>shaping</c>: a conversation with Coach open, planque there to read its tools, and the
    /// panel 780 across or more (its edge's strip aside).
    /// </summary>
    bool Shaping => coach is not null && thread is not null && Grants is not null && ThreadView.Visibility == Visibility.Visible
        && CoachScreen.Shaping(Sheet.ActualWidth - 16);

    /// <summary>Whether it showed when the thread was last drawn: crossing 780, the thread is drawn again, its plan moving.</summary>
    bool shaped;

    void OnSheetSized(object sender, SizeChangedEventArgs e)
    {
        if (Shaping != shaped) Redraw();
    }

    /// <summary>Full screen for a screenshot of "Taking shape" (<c>--page coach-wide</c>), not kept for next time.</summary>
    public void Widen()
    {
        chosen = chosen with { Full = true };
        ShowFull();
        Fit(WindowWidth);
    }

    void ShapeAgain()
    {
        if (shaped && thread is not null) ShowShape(thread);
    }

    void ShowShape(CoachThread thread)
    {
        shaped = Shaping;
        ShapeView.Visibility = shaped ? Visibility.Visible : Visibility.Collapsed;
        ShapeItems.Children.Clear();
        if (!shaped) return;
        var w = App.Words;
        var shape = CoachShape.Of(w, thread, grants ?? [], drafts);

        var head = new StackPanel { Spacing = 6 };
        head.Children.Add(Eyebrow(w.Get("coach.shape.taking_shape")));
        if ((shape.Name ?? shape.Title) is { } heading)
            head.Children.Add(new TextBlock { Text = heading, FontSize = 26, FontWeight = Microsoft.UI.Text.FontWeights.Bold, TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("TafsInk") });
        if (shape.IsEmpty) head.Children.Add(Words(w.Get("billing.conversation_goes_plan_tools_being"), 14, "TafsSoftText"));
        ShapeItems.Children.Add(head);
        if (shape.Steps.Count > 0) ShapeItems.Children.Add(ShapePlan(shape.Steps));
        if (shape.Tools.Count > 0) ShapeItems.Children.Add(ShapeTools(shape.Tools));
        if (shape.Card is { } card) ShapeItems.Children.Add(ShapeCardView(card));
    }

    TextBlock Eyebrow(string text) => new()
    {
        Text = text.ToUpper(new System.Globalization.CultureInfo(App.Words.Language)), Style = (Style)Application.Current.Resources["TafsEyebrow"],
        Foreground = Brush("TafsDim"), TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>"Plan · 2 of 4", each step with its mark; "Needs you" under the one waiting on the person; skipped, struck through.</summary>
    StackPanel ShapePlan(IReadOnlyList<ShapeStep> steps)
    {
        var w = App.Words;
        var plan = new StackPanel { Spacing = 10 };
        plan.Children.Add(Eyebrow(w.Get("coach.plan.progress", ("done", steps.Count(s => s.State == ShapeStepState.Done).ToString()),
            ("total", steps.Count.ToString()))));
        foreach (var step in steps)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var mark = StepMark(step.State switch
            {
                ShapeStepState.Done => "done",
                ShapeStepState.Failed => "failed",
                ShapeStepState.NeedsYou or ShapeStepState.Now => "active",
                _ => "pending",
            });
            mark.VerticalAlignment = VerticalAlignment.Top;
            mark.Margin = new Thickness(0, 1, 0, 0);
            row.Children.Add(mark);
            var words = new StackPanel { Spacing = 2 };
            var label = Words(step.Label, 14, step.State is ShapeStepState.ToDo or ShapeStepState.Skipped ? "TafsDimText" : "TafsInkText");
            if (step.State == ShapeStepState.Skipped) label.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
            if (step.State is ShapeStepState.NeedsYou or ShapeStepState.Now) label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            words.Children.Add(label);
            if (step.State == ShapeStepState.NeedsYou)
                words.Children.Add(new TextBlock { Text = w.Get("needs_you.title"), FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = Brush("TafsAmberText") });
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            // Read as one, as the iPad's combined element: the step, and that it needs you.
            AutomationProperties.SetName(row, step.State == ShapeStepState.NeedsYou ? step.Label + ", " + w.Get("needs_you.title") : step.Label);
            plan.Children.Add(row);
        }
        return plan;
    }

    /// <summary>Each vendor on a card, where it stands in its colour, its tools once connected and why it failed; then that no taf sees the sign-ins.</summary>
    StackPanel ShapeTools(IReadOnlyList<ShapeTool> tools)
    {
        var w = App.Words;
        var list = new StackPanel { Spacing = 10 };
        list.Children.Add(Eyebrow(w.Get("coach.tools_eyebrow")));
        foreach (var tool in tools)
        {
            var (status, color) = tool.State switch
            {
                ShapeToolState.Connected => (w.Get("connect.status.connected"), "TafsOk"),
                ShapeToolState.SignedIn => (w.Get("coach.answer.signed_in"), "TafsAmberText"),
                ShapeToolState.Waiting => (w.Get("chat.waiting_for_you"), "TafsAmberText"),
                _ => (w.Get("coach.shape.not_connected"), "TafsNo"),
            };
            var box = new StackPanel { Spacing = 3 };
            var top = new Grid { ColumnSpacing = 8 };
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(new TextBlock { Text = tool.Vendor, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("TafsInk"),
                TextTrimming = TextTrimming.CharacterEllipsis });
            var said = new TextBlock { Text = status, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(color),
                VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(said, 1);
            top.Children.Add(said);
            box.Children.Add(top);
            if (tool.Actions.Count > 0)
                box.Children.Add(new TextBlock { Text = string.Join(", ", tool.Actions), FontSize = 12, TextWrapping = TextWrapping.Wrap,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"), Foreground = Brush("TafsSoft") });
            if (tool.Why is { } why) box.Children.Add(Words(why, 12, "TafsSoftText"));
            var card = new Border { Child = box, Padding = new Thickness(12), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
                Background = Brush("TafsPanel"), BorderBrush = Brush("TafsLine") };
            AutomationProperties.SetName(card, tool.Vendor + ", " + status);
            list.Children.Add(card);
        }
        list.Children.Add(Words(w.Get("connect.sign_ins_kept_sealed_no"), 12, "TafsDimText"));
        return list;
    }

    /// <summary>The Card as KIFF drafted it, in mono: a dashed edge while a draft, "a draft until you approve it"; KIFF's green once confirmed.</summary>
    StackPanel ShapeCardView(ShapeCard card)
    {
        var w = App.Words;
        var view = new StackPanel { Spacing = 10 };
        view.Children.Add(Eyebrow(w.Get(card.Confirmed ? "card.card_confirmed_kiff" : "card.card_draft_kiff")));
        var mono = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas");
        var lines = new StackPanel { Spacing = 6, Padding = new Thickness(14) };
        lines.Children.Add(new TextBlock { Text = card.Title, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontFamily = mono,
            TextWrapping = TextWrapping.Wrap, Foreground = Brush("TafsInk") });
        foreach (var line in card.Lines.Count > 0 ? card.Lines : [w.Get("card.no_actions_short")])
            lines.Children.Add(new TextBlock { Text = line, FontSize = 12, FontFamily = mono, TextWrapping = TextWrapping.Wrap, Foreground = Brush("TafsInk") });
        // A Border draws no dashes: its edge is a rectangle behind the lines, dashed 5 and 4 while it's a draft.
        var edge = new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            RadiusX = 14, RadiusY = 14, StrokeThickness = 1, Fill = Brush("TafsPanel"),
            Stroke = card.Confirmed ? Brush("TafsOk") : Faint("TafsInk", 0.25),
        };
        if (!card.Confirmed) edge.StrokeDashArray = [5, 4];
        var box = new Grid();
        box.Children.Add(edge);
        box.Children.Add(lines);
        view.Children.Add(box);
        if (!card.Confirmed) view.Children.Add(Words(w.Get("coach.shape.draft_until_approved"), 12, "TafsDimText"));
        return view;
    }
}
