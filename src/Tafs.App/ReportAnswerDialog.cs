using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Coach;

namespace Tafs.App;

/// <summary>
/// Reports an answer Coach wrote, as the iPad's <c>ReportAnswerSheet</c>: a reason, an optional note, and Send. Sent, it
/// says a person at KIFF will read it, with Done; not sent, it says why and stays open to send again.
/// </summary>
sealed class ReportAnswerDialog : ContentDialog
{
    readonly Func<string, string, Task<ReportOutcome>> send;
    readonly RadioButtons reasons = new();
    readonly TextBox note = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 88, MaxHeight = 200 };
    readonly TextBlock tooLong;
    readonly TextBlock failed;
    bool sending;
    bool done;

    static Style Look(string key) => (Style)Application.Current.Resources[key];

    /// <summary>A theme brush in this dialog's theme, as <see cref="CoachPanel"/> finds one: Theme.xaml's dictionaries first.</summary>
    Microsoft.UI.Xaml.Media.Brush Brush(string key)
    {
        var theme = ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        foreach (var merged in Application.Current.Resources.MergedDictionaries.Prepend(Application.Current.Resources))
            if (merged.ThemeDictionaries.TryGetValue(theme, out var found) && found is ResourceDictionary d && d.TryGetValue(key, out var brush)
                && brush is Microsoft.UI.Xaml.Media.Brush b)
                return b;
        return (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[key];
    }

    static TextBlock Line(string text, double size, string style) =>
        new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Style = Look(style) };

    /// <param name="send">The reason and the note, sent; what it came to.</param>
    public ReportAnswerDialog(XamlRoot root, Func<string, string, Task<ReportOutcome>> send)
    {
        var w = App.Words;
        this.send = send;
        XamlRoot = root;
        Title = w.Get("report.menu");
        PrimaryButtonText = w.Get("common.send");
        CloseButtonText = w.Get("common.cancel");
        DefaultButton = ContentDialogButton.Primary;
        IsPrimaryButtonEnabled = false;

        reasons.Header = w.Get("report.what_wrong");
        foreach (var r in AnswerReport.Reasons) reasons.Items.Add(new RadioButton { Content = AnswerReport.Title(w, r), Tag = r });
        reasons.SelectionChanged += (_, _) => Enable();
        note.PlaceholderText = w.Get("report.note_hint");
        AutomationProperties.SetName(note, w.Get("report.note_hint"));
        note.TextChanged += (_, _) => Enable();
        tooLong = Line(w.Get("report.note_most_characters"), 13, "TafsNoText");
        tooLong.Visibility = Visibility.Collapsed;
        failed = Line("", 14, "TafsNoText");
        failed.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(failed, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 12, MinWidth = 360,
                Children = { reasons, note, tooLong, Line(w.Get("report.person_kiff_reads_your_report"), 13, "TafsDimText"), failed },
            },
        };
        PrimaryButtonClick += OnSend;
    }

    string? Reason => (reasons.SelectedItem as RadioButton)?.Tag as string;

    /// <summary>Send is off with no reason, while sending, or with a note past the limit, as the iPad's.</summary>
    void Enable()
    {
        var over = AnswerReport.TooLong(note.Text);
        tooLong.Visibility = over ? Visibility.Visible : Visibility.Collapsed;
        IsPrimaryButtonEnabled = !done && Reason is not null && !sending && !over;
    }

    async void OnSend(ContentDialog sender, ContentDialogButtonClickEventArgs e)
    {
        // The dialog stays open: it closes from Cancel, or from Done once sent.
        e.Cancel = true;
        if (Reason is not { } reason || sending || done) return;
        var deferral = e.GetDeferral();
        sending = true;
        failed.Visibility = Visibility.Collapsed;
        Enable();
        var outcome = await send(reason, note.Text);
        sending = false;
        deferral.Complete();
        if (outcome is ReportOutcome.Failed f)
        {
            failed.Text = App.Words.Get("report.not_sent_why", ("failed", f.Why));
            failed.Visibility = Visibility.Visible;
            Enable();
            return;
        }
        Thanks(outcome is ReportOutcome.Already);
    }

    /// <summary>The iPad's <c>thanks</c>: a check in amber, "Thanks…" or that it was reported already, and Done.</summary>
    void Thanks(bool already)
    {
        var w = App.Words;
        done = true;
        PrimaryButtonText = "";
        CloseButtonText = w.Get("common.done");
        DefaultButton = ContentDialogButton.Close;
        var said = Line(w.Get(already ? "report.already" : "report.thanks"), 17, "TafsInkText");
        said.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        said.TextAlignment = TextAlignment.Center;
        said.HorizontalAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetLiveSetting(said, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);
        Content = new StackPanel
        {
            Spacing = 18, MinWidth = 360, Padding = new Thickness(0, 24, 0, 24),
            Children =
            {
                new FontIcon { Glyph = "", FontSize = 40, Foreground = Brush("TafsAmberText"), HorizontalAlignment = HorizontalAlignment.Center },
                said,
            },
        };
    }
}
