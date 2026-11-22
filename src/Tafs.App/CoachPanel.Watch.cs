using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Coach;

namespace Tafs.App;

/// <summary>
/// W11, "From your watch" (docs/watch-app.md), as the iPad's <c>WatchDaysSection</c> and <c>WatchDayView</c>: in Previous
/// conversations, the days the person asked from their watch, newest first, each with how many; a day opens read only,
/// oldest first: "⌚ Sent from your watch" and when, what was said, then the taf that took it and what came of it.
/// </summary>
public sealed partial class CoachPanel
{
    /// <summary>The account's time zone (KIFF's), for the days and the turns' times; this PC's when it has none.</summary>
    public Func<string?>? AccountTimeZone { get; set; }

    string? watchDayShown;

    TimeZoneInfo AccountZone => AccountTimeZone?.Invoke() is { } id && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var z) ? z : TimeZoneInfo.Local;

    async Task AddWatchDaysAsync(int gen)
    {
        if (read is not { } reader) return;
        var (list, _) = await WatchDays.LoadAsync(reader);
        if (gen != generation || showingArchived || list is not { Days.Count: > 0 } days) return;
        var w = App.Words;
        ChatsItems.Children.Add(new TextBlock { Text = w.Get("coach.chats.from_your_watch"), Style = (Style)Application.Current.Resources["TafsEyebrow"],
            Margin = new Thickness(4, 12, 0, 0) });
        foreach (var day in days.Days)
        {
            var title = WatchDays.Title(w, day.Date, days.Today, AccountZone, DateTimeOffset.UtcNow);
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new FontIcon { Glyph = "", FontSize = 15 });
            var name = Words(title, 15, "TafsInkText");
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var count = Words(day.Count.ToString(global::Tafs.Core.Words.Locale.Culture(w.Language)), 13, "TafsDimText");
            Grid.SetColumn(count, 2);
            row.Children.Add(count);
            var open = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(14, 12, 14, 12), Style = (Style)Application.Current.Resources["TafsQuietButton"] };
            AutomationProperties.SetName(open, $"{title}, {day.Count}");
            open.Click += (_, _) => _ = ShowWatchDayAsync(day.Date, title);
            ChatsItems.Children.Add(new Border { Child = open, CornerRadius = new CornerRadius(12), Background = Brush("TafsPanel") });
        }
    }

    /// <summary>One day from the watch, read only: a reply goes on in a Coach chat, as any other.</summary>
    async Task ShowWatchDayAsync(string date, string title)
    {
        if (read is not { } reader) return;
        var w = App.Words;
        var gen = generation;
        watchDayShown = date;
        ChatsTitle.Text = title;
        ChatsItems.Children.Clear();
        ChatsItems.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20, Margin = new Thickness(0, 20, 0, 0) });
        var answer = await reader(WatchDays.DayPath(date));
        if (gen != generation || watchDayShown != date) return;
        ChatsItems.Children.Clear();
        if (answer.Answer is not { } a)
        {
            ChatsItems.Children.Add(Words(w.Get("coach.conversation_not_loaded", ("why", answer.Why ?? "")), 14, "TafsNoText"));
            var again = new Button { Content = w.Get("common.try_again"), Style = (Style)Application.Current.Resources["TafsOutlineButton"] };
            again.Click += (_, _) => _ = ShowWatchDayAsync(date, title);
            ChatsItems.Children.Add(again);
            return;
        }
        foreach (var turn in WatchDays.ReadDay(a))
        {
            var lines = new StackPanel { Spacing = 6 };
            var when = WatchDays.Time(turn.SaidAt, AccountZone);
            lines.Children.Add(Words(w.Get("coach.chats.sent_from_your_watch") + (when is null ? "" : " · " + when), 12, "TafsDimText"));
            lines.Children.Add(Words(turn.Said, 15, "TafsInkText"));
            var outcome = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Style = (Style)Application.Current.Resources["TafsDimText"] };
            if (turn.TafName is { } taf) outcome.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = taf + "  ", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            outcome.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = turn.Outcome });
            lines.Children.Add(outcome);
            ChatsItems.Children.Add(new Border { Child = lines, Padding = new Thickness(14, 10, 14, 10), CornerRadius = new CornerRadius(12),
                Background = Brush("TafsPanel"), Margin = new Thickness(turn.ReplyTo is null ? 0 : 16, 0, 0, 0) });
        }
    }
}
