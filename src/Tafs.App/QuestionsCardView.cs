using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Tafs.Core.Coach;

namespace Tafs.App;

/// <summary>What a questions card keeps while the thread is drawn again: what's picked, and whether it's put away to chat.</summary>
sealed class QuestionsCardState
{
    public QuestionSelection Selection { get; } = new();
    public bool Chatting { get; set; }
}

/// <summary>
/// Several questions Coach asks at once, as the iPad's <c>QuestionsCard</c>: every question, choice and line is Coach's. The
/// app adds "Something else…" under each question, for the person's own words, and "Chat about this", which puts the card
/// away. Answered, it's one line with the answers.
/// </summary>
sealed class QuestionsCardView : StackPanel
{
    readonly ShownCard shown;
    readonly CardReply? answer;
    readonly QuestionsCardState state;
    readonly bool busy;
    readonly string? note;
    readonly Action<string>? send;
    readonly Func<string, Brush> brush;
    readonly List<Action> refresh = [];

    /// <param name="busy">Answered here and not yet in the thread: nothing more can be picked or sent.</param>
    /// <param name="note">Why the last answer didn't reach Setup, as the iPad's <c>CardNote</c>.</param>
    /// <param name="send">None for someone who may not use Coach, or a screenshot: Send stays off.</param>
    public QuestionsCardView(ShownCard shown, CardReply? answer, QuestionsCardState state, bool busy, string? note,
        Action<string>? send, Func<string, Brush> brush)
    {
        (this.shown, this.answer, this.state, this.busy, this.note, this.send, this.brush) = (shown, answer, state, busy, note, send, brush);
        Build();
    }

    IReadOnlyList<CardQuestion> Questions => shown.Card.Questions ?? [];

    int Answered => Questions.Count(q => state.Selection.Answer(q) is not null);

    static Style Look(string key) => (Style)Application.Current.Resources[key];

    void Build()
    {
        var w = App.Words;
        Children.Clear();
        refresh.Clear();
        if (answer is { } a)
        {
            Children.Add(Outcome(w.Get("coach.questions.your_answers") + (a.Words ?? QuestionAnswers.Words(w, a.Text, Questions) ?? a.Text)));
            return;
        }
        if (state.Chatting)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(CoachPanel.Words(w.Get("coach.questions.talking"), 13, "TafsSoftText"));
            var again = new Button { Content = w.Get("coach.questions.show_again"), FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = brush("TafsCoach"), Style = Look("TafsQuietButton"), Padding = new Thickness(0) };
            again.Click += (_, _) => { state.Chatting = false; Build(); };
            row.Children.Add(again);
            Children.Add(row);
            return;
        }
        var panel = new StackPanel { Spacing = 12 };
        if (shown.Card.Text is { Length: > 0 } text) panel.Children.Add(CoachPanel.Words(text, 15, "TafsInkText"));
        var head = new Grid { ColumnSpacing = 8 };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock { Text = (shown.Card.Title ?? w.Get("coach.setup_needs")).ToUpper(new System.Globalization.CultureInfo(w.Language)),
            Style = Look("TafsEyebrow"), TextWrapping = TextWrapping.Wrap });
        var count = new TextBlock { FontSize = 12, FontFamily = new FontFamily("Cascadia Mono, Consolas"), Style = Look("TafsDimText") };
        Grid.SetColumn(count, 1);
        head.Children.Add(count);
        refresh.Add(() => count.Text = w.Get("coach.questions.answered_of", ("answered", Answered.ToString()), ("total", Questions.Count.ToString())));
        panel.Children.Add(head);
        for (var i = 0; i < Questions.Count; i++) panel.Children.Add(Question(Questions[i], i + 1));
        panel.Children.Add(new Rectangle { Height = 1, Style = Look("TafsLineRule") });
        var actions = new WrapRow { Spacing = 12 };
        var sending = new Button { Content = w.Get("coach.questions.send"), Padding = new Thickness(18, 0, 18, 0) };
        sending.Click += (_, _) => Send();
        refresh.Add(() =>
        {
            sending.IsEnabled = Answered == Questions.Count && !busy && send is not null;
            sending.Style = Look(sending.IsEnabled ? "TafsPrimary" : "TafsPrimaryOff");
        });
        var chat = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        chat.Children.Add(new FontIcon { Glyph = "\uE8BD", FontSize = 15, Foreground = brush("TafsCoach") });
        chat.Children.Add(new TextBlock { Text = w.Get("coach.questions.chat_about"), FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = brush("TafsCoach") });
        var talk = new Button { Content = chat, Style = Look("TafsQuietButton"), MinHeight = 44, Padding = new Thickness(4, 0, 4, 0) };
        talk.Click += (_, _) => { state.Chatting = true; Build(); };
        actions.Children.Add(sending);
        actions.Children.Add(talk);
        panel.Children.Add(actions);
        if (note is not null) panel.Children.Add(Note(note));
        Children.Add(new Border { Style = Look("TafsCard"), Child = panel });
        Refresh();
    }

    void Refresh()
    {
        foreach (var r in refresh) r();
    }

    StackPanel Question(CardQuestion q, int number)
    {
        var w = App.Words;
        var box = new StackPanel { Spacing = 10 };
        var title = CoachPanel.Words($"{number}. {q.Text}", 16, "TafsInkText");
        title.FontWeight = FontWeights.SemiBold;
        box.Children.Add(title);
        var choices = new WrapRow { Spacing = 8 };
        foreach (var o in q.Options)
            choices.Children.Add(Choice(o.Label, o.Description, () => state.Selection.IsPicked(q, o.Value), () => state.Selection.Pick(q, o.Value)));
        choices.Children.Add(Choice(w.Get("coach.questions.something_else"), null, () => state.Selection.OwnOpen(q), () => state.Selection.ToggleOwn(q)));
        box.Children.Add(choices);
        var own = new TextBox { Text = state.Selection.OwnText(q), PlaceholderText = w.Get("coach.own_answer"), FontSize = 15, TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            BorderBrush = brush("TafsCoach"), Background = brush("TafsPanel") };
        AutomationProperties.SetName(own, w.Get("coach.questions.own_answer_to", ("text", q.Text)));
        // Typing only recounts: the field keeps its focus.
        own.TextChanged += (_, _) => { state.Selection.SetOwn(q, own.Text); Refresh(); };
        refresh.Add(() => own.Visibility = state.Selection.OwnOpen(q) ? Visibility.Visible : Visibility.Collapsed);
        box.Children.Add(own);
        if (q.Note is { Length: > 0 } said) box.Children.Add(CoachPanel.Words(said, 13, "TafsSoftText"));
        return box;
    }

    /// <summary>A choice, as the iPad's: Coach's blue when picked, else the panel with a faint edge; its detail under it.</summary>
    ToggleButton Choice(string label, string? detail, Func<bool> on, Action pick)
    {
        var words = new TextBlock { Text = label, FontSize = 15, TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 2 };
        content.Children.Add(words);
        var more = detail is { Length: > 0 } ? new TextBlock { Text = detail, FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap } : null;
        if (more is not null) content.Children.Add(more);
        var button = new ToggleButton { Content = content, Style = Look("TafsChoice"), IsEnabled = !busy };
        button.Click += (_, _) => { pick(); Refresh(); };
        refresh.Add(() =>
        {
            var picked = on();
            button.IsChecked = picked;
            words.FontWeight = picked ? FontWeights.SemiBold : FontWeights.Normal;
            words.Foreground = brush(picked ? "TafsOnFill" : "TafsInk");
            if (more is not null) more.Foreground = words.Foreground;
            button.Background = brush(picked ? "TafsCoach" : "TafsPanel");
            button.BorderBrush = picked ? brush("TafsCoach") : Faint("TafsInk", 0.18);
        });
        return button;
    }

    Brush Faint(string key, double alpha)
    {
        var c = ((SolidColorBrush)brush(key)).Color;
        c.A = (byte)Math.Round(c.A * alpha);
        return new SolidColorBrush(c);
    }

    void Send()
    {
        var answers = new Dictionary<string, QuestionAnswer>();
        foreach (var q in Questions)
            if (state.Selection.Answer(q) is { } a) answers[q.Id] = a;
        if (answers.Count != Questions.Count) return;
        send?.Invoke(QuestionAnswers.Encode(answers));
    }

    /// <summary>An answered card, folded to one line, as the iPad's <c>OutcomeLine</c>: a tick in the green, its words soft, at 14.</summary>
    Border Outcome(string text)
    {
        var line = new Grid { ColumnSpacing = 8 };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition());
        line.Children.Add(new FontIcon { Glyph = "\uEC61", FontSize = 14, Foreground = brush("TafsOk"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
        var words = CoachPanel.Words(text, 14, "TafsSoftText");
        Grid.SetColumn(words, 1);
        line.Children.Add(words);
        var raised = ((SolidColorBrush)brush("TafsRaised")).Color;
        raised.A = (byte)Math.Round(raised.A * 0.6);
        return new Border { Child = line, Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(raised) };
    }

    /// <summary>Why a tap ended without reaching Setup, as the iPad's <c>CardNote</c>: an ⓘ and the words, soft, at 13.</summary>
    Grid Note(string text)
    {
        var line = new Grid { ColumnSpacing = 6 };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition());
        line.Children.Add(new FontIcon { Glyph = "\uE946", FontSize = 13, Foreground = brush("TafsSoft"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
        var words = CoachPanel.Words(text, 13, "TafsSoftText");
        Grid.SetColumn(words, 1);
        line.Children.Add(words);
        return line;
    }
}
