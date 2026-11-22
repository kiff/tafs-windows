using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Coach;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// Coach's dial card (K4), as the iPad's <c>KnobDraftCard</c>: the dial version Coach drafted for a taf, drawn from tafs cloud's
/// words for it (<c>GET …/knob</c>'s draft), never Coach's. Live already, it says so; replaced since, it says that. For whoever
/// manages the taf, Confirm opens the taf's Card › Versions, where the version is installed with its one signed confirmation,
/// and Change goes back to Coach's box.
/// </summary>
public sealed partial class CoachPanel
{
    /// <summary>Each dial card's read, by the taf: the dial, or none while it's read or when it couldn't be.</summary>
    readonly Dictionary<string, TafKnob?> knobCards = [];
    readonly HashSet<string> knobReading = [];
    readonly Dictionary<string, string> knobFailed = [];

    Border KnobCard(CoachCard c)
    {
        var w = App.Words;
        var lines = new StackPanel { Spacing = 10 };
        var taf = c.TafId is { } id ? Tafs?.Invoke().FirstOrDefault(t => t.Id == id) : null;
        if (taf is not null) lines.Children.Add(new TextBlock { Text = taf.Name + w.Get("coach.will_work_like"), Style = (Style)Application.Current.Resources["TafsEyebrow"] });
        if (c.TafId is { } tafId && !knobCards.ContainsKey(tafId) && knobReading.Add(tafId)) _ = ReadKnobCardAsync(tafId);
        var knob = c.TafId is { } k && knobCards.TryGetValue(k, out var read) ? read : null;
        if (knob is null && c.TafId is { } failedTaf && knobFailed.TryGetValue(failedTaf, out var why))
            lines.Children.Add(Words(w.Get("common.could_not_read_why", ("what", w.Get("setup.starts_when")), ("why", why)), 13, "TafsNoText"));
        else if (knob is null)
            lines.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left });
        else if (c.Version is { } version && knob.Version is { } live && live >= version)
            lines.Children.Add(Words(w.Get("knob.live"), 14, "TafsOkText"));
        else if (knob.DraftVersion is { } drafted && drafted == c.Version || knob.Pending?.Version == c.Version)
        {
            foreach (var line in knob.DraftWords) lines.Children.Add(Words(line, 15, "TafsInkText"));
            if (taf is not null && c.TafId is { } open)
            {
                var confirm = new Button { Content = w.Get("knob.confirm_with", ("biometry", CoachScreen.Biometry)), Style = (Style)Application.Current.Resources["TafsPrimary"],
                    Padding = new Thickness(18, 8, 18, 8) };
                AutomationProperties.SetAutomationId(confirm, "knob.confirm");
                confirm.Click += (_, _) => VersionsChosen?.Invoke(open);
                var change = new Button { Content = w.Get("knob.change"), Style = (Style)Application.Current.Resources["TafsOutlineButton"], Padding = new Thickness(18, 8, 18, 8) };
                change.Click += (_, _) => CoachDraft.Focus(FocusState.Programmatic);
                lines.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { confirm, change } });
                lines.Children.Add(Words(w.Get("knob.one_confirmation"), 12, "TafsDimText"));
            }
        }
        else lines.Children.Add(Words(w.Get("knob.replaced"), 14, "TafsDimText"));
        return new Border { Child = lines, Padding = new Thickness(14), CornerRadius = new CornerRadius(14), Background = Brush("TafsRaised") };
    }

    async Task ReadKnobCardAsync(string taf)
    {
        if (Read is not { } read) return;
        var answer = await read(TafKnob.Path(taf));
        knobReading.Remove(taf);
        knobCards[taf] = answer.Answer is { } a ? TafKnob.Read(a) : null;
        if (knobCards[taf] is null) knobFailed[taf] = answer.Why ?? App.Words.Get("error.ours");
        Redraw();
    }
}
