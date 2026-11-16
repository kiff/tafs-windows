using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.Calendar;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// Calendar, as the iPad's <c>CalendarView</c>: the tafs' work to come, from tafs cloud (<c>GET /v1/calendar</c>, one read for
/// the selected month's six weeks). Wide (the page 700 across, wider than tall, 600 high), a month navigator on the left with
/// each day's marks, the calendar beside it (Day and Week as hours, Month as a grid) and an agenda under them; compact, the
/// strip of days and the selected day's list. A taf's work opens the taf.
/// </summary>
public sealed partial class CalendarPage : Page
{
    const double HourHeight = 44;
    DateOnly today;
    CalendarState state = null!;
    CalendarLayout layout = CalendarLayout.Compact;

    /// <summary>How the page reads tafs cloud as the session; none signed out or on the sample, and every day has nothing yet.</summary>
    public static Func<string, Task<CloudRead>>? Read { get; set; }

    /// <summary>A taf's page, by tafs cloud's id, when it's on Home; none for one this person no longer has listed.</summary>
    public static Func<string, Action?>? Opener { get; set; }

    CalendarRead? read;
    string? readWhy;
    (DateOnly, DateOnly)? reading;
    /// <summary>The session the kept read is for: another one starts from nothing.</summary>
    Func<string, Task<CloudRead>>? readFor;

    public CalendarPage() => InitializeComponent();

    /// <param name="e">The view to open on (Day for the reference set's day screenshot); Week otherwise.</param>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var w = App.Words;
        today = DateOnly.FromDateTime(DateTime.Now);
        state = CalendarScreen.Opening(today) with { Mode = e.Parameter as CalendarMode? ?? CalendarMode.Week };
        NavTitle.Text = CalendarScreen.Heading(w);
        // The date in the person's language, "November 15, 2026" or "15 novembre 2026": the full month, so juin and juillet never read alike.
        // A template, not a pattern: Windows' formatter for the language decides the order, the particles and the case.
        Picker.Language = w.Language == "pt" ? "pt-PT" : w.Language;
        // As the iPad's compact DatePicker: the month short ("Nov 15, 2026"), in the language's own order.
        Picker.DateFormat = "day month.abbreviated year.full";
        AutomationProperties.SetName(Modes, w.Get("calendar.view_picker"));
        AutomationProperties.SetName(Picker, w.Get("calendar.date_picker"));
        AutomationProperties.SetHelpText(Picker, w.Get("calendar.choose_date"));
        AutomationProperties.SetName(NavPrevious, w.Get("calendar.previous_month"));
        AutomationProperties.SetName(NavNext, w.Get("calendar.next_month"));
        TryAgain.Content = w.Get("common.try_again");
        Show();
    }

    /// <summary>Wide or compact by the page's own size, as the iPad's <c>CalendarLayout.of</c>; drawn again when it changes.</summary>
    void OnSized(object sender, SizeChangedEventArgs e)
    {
        var now = CalendarScreen.LayoutOf(e.NewSize.Width, e.NewSize.Height);
        if (now == layout || state is null) return;
        layout = now;
        Show();
    }

    /// <summary>The read for the selected date's range, unless it's read or being read; a later answer for another range is dropped.</summary>
    async Task LoadAsync()
    {
        if (Read is not { } cloud) return;
        if (!ReferenceEquals(readFor, cloud)) (read, readWhy, reading, readFor) = (null, null, null, cloud);
        var range = CalendarWork.Range(state.Selected);
        if (CalendarWork.Current(read, state.Selected) is not null || reading == range) return;
        (reading, readWhy) = (range, null);
        var answer = await cloud(CalendarRead.Path(range.From, range.To));
        // Another range asked since, or another session: the last account's work is never drawn for the next.
        if (!CalendarWork.Lands(range, reading, cloud, Read)) return;
        reading = null;
        if (answer.Answer is { } a && CalendarRead.Read(a) is { } r) read = r;
        else readWhy = answer.Why ?? App.Words.Get("kiff.kiff_did_not_answer_expected");
        Show();
    }

    void OnTryAgain(object sender, RoutedEventArgs e) => _ = LoadAsync();

    IReadOnlyList<CalendarEvent> Events => CalendarWork.Current(read, state.Selected)?.Events ?? [];

    void Show()
    {
        var w = App.Words;
        _ = LoadAsync();
        var wide = layout == CalendarLayout.Wide;
        Title.Text = CalendarScreen.Title(w, state);
        var atToday = state.Selected == today;
        TodayPill.Content = CalendarScreen.Today(w);
        TodayPill.Style = (Style)Application.Current.Resources[atToday ? "TafsTodayPillAt" : "TafsTodayPill"];
        var (previous, next) = CalendarScreen.Arrows(w, state.Mode);
        AutomationProperties.SetName(Previous, previous);
        AutomationProperties.SetName(Next, next);
        Picker.DateChanged -= OnPicked;
        Picker.Date = new DateTimeOffset(state.Selected.ToDateTime(TimeOnly.MinValue));
        Picker.DateChanged += OnPicked;

        Modes.Children.Clear();
        Modes.ColumnDefinitions.Clear();
        foreach (var (mode, title) in CalendarScreen.Modes(w))
        {
            Modes.ColumnDefinitions.Add(new ColumnDefinition());
            var segment = new Button { Content = title, Style = (Style)Application.Current.Resources[mode == state.Mode ? "TafsSegmentChosen" : "TafsSegment"] };
            if (mode == state.Mode) AutomationProperties.SetItemStatus(segment, "selected");
            segment.Click += (_, _) => { state = state with { Mode = mode }; Show(); };
            Grid.SetColumn(segment, Modes.Children.Count);
            Modes.Children.Add(segment);
        }
        ModesTrack.Width = wide ? 320 : double.NaN;
        ModesTrack.HorizontalAlignment = wide ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        ShowReadState(w);

        // Wide: the navigator beside, the agenda under; compact: the strip and the selected day's list.
        NavColumn.Width = wide ? new GridLength(304) : new GridLength(0);
        Navigator.Visibility = Seen(wide);
        Agenda.Visibility = Seen(wide);
        NavTitle.Visibility = Seen(!wide);
        var timed = state.Mode == CalendarMode.Day || (wide && state.Mode == CalendarMode.Week);
        DayStripScroll.Visibility = Seen(!wide && state.Mode == CalendarMode.Day);
        WeekStrip.Visibility = Seen(!wide && state.Mode == CalendarMode.Week);
        HoursScroll.Visibility = TimeHeads.Visibility = Seen(timed);
        Month.Visibility = Seen(state.Mode == CalendarMode.Month);
        DayList.Visibility = Seen(!wide && state.Mode != CalendarMode.Day);

        if (!wide && state.Mode == CalendarMode.Day)
        {
            DayStrip.Children.Clear();
            foreach (var day in CalendarScreen.StripDays(state)) DayStrip.Children.Add(Tile(day, fills: false));
            // The selected day's week first, as the iPad scrolls it, once the strip is laid out.
            var at = CalendarScreen.StripDays(state).ToList().IndexOf(CalendarScreen.WeekStart(state.Selected));
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => DayStripScroll.ChangeView(at * (46 + 6), null, null, true));
        }
        if (!wide && state.Mode == CalendarMode.Week)
        {
            WeekStrip.Children.Clear();
            WeekStrip.ColumnDefinitions.Clear();
            foreach (var day in CalendarScreen.StripDays(state))
            {
                WeekStrip.ColumnDefinitions.Add(new ColumnDefinition());
                var tile = Tile(day, fills: true);
                Grid.SetColumn(tile, WeekStrip.Children.Count);
                WeekStrip.Children.Add(tile);
            }
        }
        if (timed) ShowHours(w, state.Mode == CalendarMode.Day ? [state.Selected] : CalendarScreen.Week(state.Selected));
        else TimeEmpty.Visibility = Visibility.Collapsed;
        if (state.Mode == CalendarMode.Month) ShowMonth(w, Month, large: wide);
        if (!wide && state.Mode != CalendarMode.Day) ShowDayList(w);
        if (wide)
        {
            NavMonth.Text = CalendarScreen.Title(w, state with { Mode = CalendarMode.Month });
            ShowMonth(w, NavMonthGrid, large: false);
            ShowAgenda(w);
        }
    }

    /// <summary>Reading the range; why it wasn't read, with Try again; or tafs cloud's words when halted or some were left out.</summary>
    void ShowReadState(Tafs.Core.Words.WordBook w)
    {
        var current = CalendarWork.Current(read, state.Selected);
        var said = Read is null ? null
            : readWhy is { } why ? why
            : current is null ? w.Get("calendar.reading")
            : current.Halted || current.More ? current.Words : null;
        ReadState.Visibility = Seen(said is not null);
        Reading.IsActive = Read is not null && readWhy is null && current is null;
        Reading.Visibility = Seen(Reading.IsActive);
        ReadWords.Text = said ?? "";
        ReadWords.Style = (Style)Application.Current.Resources[readWhy is not null || current?.Halted == true ? "TafsNoText" : "TafsDimText"];
        TryAgain.Visibility = Seen(readWhy is not null);
    }

    /// <summary>
    /// The hours, as the iPad's <c>timeGrid</c>: with more than one day, each day's weekday and number on top (a tap opens it in
    /// Day); then 00:00 to 23:00, each day's work in its hour as a chip in its taf's colour ("08:00 · Morning inbox", ×4 for a
    /// dense one), opened at the first hour of work, else 08:00. Nothing that week or day says so, once read.
    /// </summary>
    void ShowHours(Tafs.Core.Words.WordBook w, IReadOnlyList<DateOnly> days)
    {
        var events = Events;
        var many = days.Count > 1;
        void Columns(Grid g)
        {
            g.ColumnDefinitions.Clear();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            foreach (var _ in days) g.ColumnDefinitions.Add(new ColumnDefinition());
        }
        TimeHeads.Children.Clear();
        Columns(TimeHeads);
        TimeHeads.Visibility = Seen(many);
        for (var i = 0; i < days.Count && many; i++)
        {
            var day = days[i];
            var head = new StackPanel { Spacing = 1, HorizontalAlignment = HorizontalAlignment.Center };
            head.Children.Add(Text(CalendarScreen.WeekdayName(w, day), 11, Microsoft.UI.Text.FontWeights.Bold, "TafsDimText"));
            var number = Text(day.Day.ToString(CultureInfo.InvariantCulture), 15, day == today ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                day == today ? "TafsAmberWords" : "TafsInkText");
            head.Children.Add(number);
            var open = new Button { Content = head, Style = (Style)Application.Current.Resources["TafsQuietButton"], HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(open, CalendarScreen.DayHeading(w, day));
            open.Click += (_, _) => { state = new CalendarState(CalendarMode.Day, day); Show(); };
            Grid.SetColumn(open, i + 1);
            TimeHeads.Children.Add(open);
        }

        Hours.Children.Clear();
        var slots = days.Select(d => CalendarWork.Slots(events, d)).ToList();
        for (var hour = 0; hour < 24; hour++)
        {
            var row = new Grid { MinHeight = HourHeight, ColumnSpacing = 4, BorderThickness = new Thickness(0, 1, 0, 0),
                Style = (Style)Application.Current.Resources["TafsHourGrid"] };
            Columns(row);
            var label = Text($"{hour:00}:00", 11, Microsoft.UI.Text.FontWeights.Normal, "TafsDimText");
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.VerticalAlignment = VerticalAlignment.Top;
            label.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            label.Margin = new Thickness(0, 3, 0, 0);
            row.Children.Add(label);
            for (var i = 0; i < days.Count; i++)
            {
                if (!slots[i].TryGetValue(hour, out var inHour)) continue;
                var chips = new StackPanel { Spacing = 2, Margin = new Thickness(0, 3, 0, 3) };
                foreach (var slot in inHour) chips.Children.Add(SlotChip(w, slot, compact: many));
                Grid.SetColumn(chips, i + 1);
                row.Children.Add(chips);
            }
            Hours.Children.Add(row);
        }
        var first = CalendarWork.FirstHour(events, days);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => HoursScroll.ChangeView(null, first * HourHeight, null, true));
        var nothing = CalendarWork.Current(read, state.Selected) is not null && days.All(d => CalendarWork.On(events, d).Count == 0);
        TimeEmpty.Text = w.Get(many ? "calendar.nothing_this_week" : "calendar.nothing_on_day");
        TimeEmpty.Visibility = Seen(nothing);
    }

    /// <summary>A taf's work in an hour: its colour's dot, "08:00 · Morning inbox" (the name alone in a week), ×4 for a dense one.</summary>
    UIElement SlotChip(Tafs.Core.Words.WordBook w, CalendarSlot slot, bool compact)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        line.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 7, Height = 7, Fill = RecordLineView.Colour(slot.Color), VerticalAlignment = VerticalAlignment.Center });
        line.Children.Add(new TextBlock { Text = compact ? slot.Name : $"{slot.First} · {slot.Name}", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, Style = (Style)Application.Current.Resources["TafsInkText"] });
        if (slot.Count > 1) line.Children.Add(new TextBlock { Text = "×" + slot.Count.ToString(CultureInfo.InvariantCulture), FontSize = 11,
            Style = (Style)Application.Current.Resources["TafsSoftText"] });
        var colour = ((SolidColorBrush)RecordLineView.Colour(slot.Color)).Color;
        var chip = new Border { Child = line, Padding = new Thickness(6, 3, 6, 3), CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(colour) { Opacity = 0.16 } };
        var spoken = slot.Count > 1 ? w.Count("calendar.slot_times", slot.Count, ("name", slot.Name), ("first", slot.First))
            : w.Get("calendar.slot_at", ("name", slot.Name), ("first", slot.First));
        return Opening(slot.Taf, chip, spoken);
    }

    /// <summary>Compact, Week and Month: the selected day's heading, then its work, or that nothing is scheduled once read.</summary>
    void ShowDayList(Tafs.Core.Words.WordBook w)
    {
        DayList.Children.Clear();
        DayList.Children.Add(new TextBlock { Text = CalendarScreen.DayHeading(w, state.Selected).ToUpper(CultureInfo.GetCultureInfo(w.Language)),
            Style = (Style)Application.Current.Resources["TafsEyebrow"] });
        var on = CalendarWork.On(Events, state.Selected);
        if (CalendarWork.Current(read, state.Selected) is not null && on.Count == 0) DayList.Children.Add(Empty(w.Get("calendar.nothing_on_day")));
        foreach (var e in on) DayList.Children.Add(EventRow(e));
    }

    /// <summary>Wide: the agenda of the days shown, each day's heading (today in amber) over its work; tafs cloud's words when none.</summary>
    void ShowAgenda(Tafs.Core.Words.WordBook w)
    {
        Agenda.Children.Clear();
        Agenda.Children.Add(new TextBlock { Text = w.Get("calendar.agenda").ToUpper(CultureInfo.GetCultureInfo(w.Language)),
            Style = (Style)Application.Current.Resources["TafsEyebrow"] });
        var groups = CalendarWork.Agenda(Events, CalendarWork.Shown(state));
        if (CalendarWork.Current(read, state.Selected) is { } current && groups.Count == 0)
            Agenda.Children.Add(Empty(current.Words.Length == 0 ? w.Get("calendar.nothing_these_days") : current.Words));
        foreach (var (day, events) in groups)
        {
            var group = new StackPanel { Spacing = 6 };
            group.Children.Add(new TextBlock { Text = CalendarScreen.DayHeading(w, day), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Style = (Style)Application.Current.Resources[day == today ? "TafsAmberWords" : "TafsInkText"] });
            foreach (var e in events) group.Children.Add(EventRow(e));
            Agenda.Children.Add(group);
        }
    }

    /// <summary>A taf's work: its time (13 mono), its colour's dot, its name and tafs cloud's words, on a panel; it opens the taf.</summary>
    UIElement EventRow(CalendarEvent e)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock { Text = e.Time, FontSize = 13, FontFamily = new FontFamily("Cascadia Mono, Consolas"), VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["TafsSoftText"] });
        var dot = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 9, Height = 9, Fill = RecordLineView.Colour(e.Color), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(dot, 1);
        row.Children.Add(dot);
        var words = new StackPanel { Spacing = 2 };
        words.Children.Add(new TextBlock { Text = e.Name, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            Style = (Style)Application.Current.Resources["TafsInkText"] });
        if (e.Words.Length > 0) words.Children.Add(new TextBlock { Text = e.Words, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis,
            Style = (Style)Application.Current.Resources["TafsDimText"] });
        Grid.SetColumn(words, 2);
        row.Children.Add(words);
        var panel = new Border { Child = row, Padding = new Thickness(10), CornerRadius = new CornerRadius(10), Background = ChatViews.ThemeBrush(ActualTheme, "TafsPanel") };
        return Opening(e.Taf, panel, $"{e.Time}, {e.Name}, {e.Words}");
    }

    /// <summary>What opens a taf, when it's on Home: a quiet button around it; else the drawing alone, read as its words.</summary>
    UIElement Opening(string taf, UIElement content, string spoken)
    {
        if (Opener?.Invoke(taf) is not { } open)
        {
            AutomationProperties.SetName(content, spoken);
            return content;
        }
        var b = new Button { Content = content, Style = (Style)Application.Current.Resources["TafsBox"], HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(b, spoken);
        b.Click += (_, _) => open();
        return b;
    }

    TextBlock Empty(string words) => new() { Text = words, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6),
        Style = (Style)Application.Current.Resources["TafsDimText"] };

    /// <summary>A day in the strip: its weekday (11 bold) and number (17 semibold), and its tafs' marks; the selected one amber, today ringed.</summary>
    Button Tile(DateOnly day, bool fills)
    {
        var chosen = day == state.Selected;
        var lines = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(Text(CalendarScreen.WeekdayName(App.Words, day), 11, Microsoft.UI.Text.FontWeights.Bold, chosen ? "TafsOnAmberText" : "TafsDimText"));
        lines.Children.Add(Text(day.Day.ToString(CultureInfo.InvariantCulture), 17, Microsoft.UI.Text.FontWeights.SemiBold, chosen ? "TafsOnAmberText" : "TafsInkText"));
        lines.Children.Add(Marks(day, chosen));
        var tile = new Button
        {
            Content = lines,
            Style = (Style)Application.Current.Resources[chosen ? "TafsDayChosen" : day == today ? "TafsDayToday" : "TafsDay"],
            Width = fills ? double.NaN : 46,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(tile, CalendarScreen.DayHeading(App.Words, day));
        if (chosen) AutomationProperties.SetItemStatus(tile, "selected");
        tile.Click += (_, _) => { state = state with { Selected = day }; Show(); };
        return tile;
    }

    /// <summary>A day's marks: the colours of its tafs with work, at most three, 5 across; white on the selected amber.</summary>
    StackPanel Marks(DateOnly day, bool onAmber)
    {
        var marks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Height = 5, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var colour in CalendarWork.Marks(Events, day))
            marks.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 5, Height = 5,
                Fill = onAmber ? new SolidColorBrush(Microsoft.UI.Colors.White) : RecordLineView.Colour(colour) });
        return marks;
    }

    /// <summary>
    /// A month: the weekdays (11 bold, dim), then six weeks; days of other months dim, the selected one on amber, today ringed,
    /// each with its marks; large (the wide Month view), 64 high with how many firings it has.
    /// </summary>
    void ShowMonth(Tafs.Core.Words.WordBook w, Grid month, bool large)
    {
        month.Children.Clear();
        month.ColumnDefinitions.Clear();
        month.RowDefinitions.Clear();
        for (var c = 0; c < 7; c++) month.ColumnDefinitions.Add(new ColumnDefinition());
        for (var r = 0; r < 7; r++) month.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var grid = CalendarScreen.MonthGrid(state.Selected);
        for (var c = 0; c < 7; c++)
        {
            var name = Text(CalendarScreen.WeekdayName(w, grid[c]), 11, Microsoft.UI.Text.FontWeights.Bold, "TafsDimText");
            Grid.SetColumn(name, c);
            month.Children.Add(name);
        }
        var events = Events;
        for (var i = 0; i < grid.Count; i++)
        {
            var day = grid[i];
            var chosen = day == state.Selected;
            var inMonth = day.Month == state.Selected.Month;
            var lines = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
            lines.Children.Add(Text(day.Day.ToString(CultureInfo.InvariantCulture), large ? 15 : 13,
                chosen ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal, inMonth ? "TafsInkText" : "TafsDimText"));
            lines.Children.Add(Marks(day, onAmber: false));
            var count = CalendarWork.Count(events, day);
            if (large && count > 0) lines.Children.Add(Text(count.ToString(CultureInfo.InvariantCulture), 11, Microsoft.UI.Text.FontWeights.Normal, "TafsSoftText"));
            var cell = new Button
            {
                Content = lines,
                Opacity = inMonth ? 1 : 0.6,
                MinHeight = large ? 64 : 38,
                // Selected and today are separate marks, as on the iPad: the fill, and the ring.
                Style = (Style)Application.Current.Resources["TafsMonth" + (chosen ? "Chosen" : "") + (day == today ? "Today" : chosen ? "" : "Day")],
            };
            AutomationProperties.SetName(cell, count > 0 ? w.Count("calendar.day_scheduled", count, ("day", CalendarScreen.DayHeading(w, day))) : CalendarScreen.DayHeading(w, day));
            cell.Click += (_, _) => { state = state with { Selected = day }; Show(); };
            Grid.SetRow(cell, i / 7 + 1);
            Grid.SetColumn(cell, i % 7);
            month.Children.Add(cell);
        }
    }

    static TextBlock Text(string text, double size, Windows.UI.Text.FontWeight weight, string style) =>
        new() { Text = text, FontSize = size, FontWeight = weight, HorizontalAlignment = HorizontalAlignment.Center, Style = (Style)Application.Current.Resources[style] };

    static Visibility Seen(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    void OnToday(object sender, RoutedEventArgs e) { if (state.Selected != today) { state = state with { Selected = today }; Show(); } }
    void OnPrevious(object sender, RoutedEventArgs e) { state = CalendarScreen.Step(state, -1); Show(); }
    void OnNext(object sender, RoutedEventArgs e) { state = CalendarScreen.Step(state, 1); Show(); }
    void OnNavPrevious(object sender, RoutedEventArgs e) { state = state with { Selected = state.Selected.AddMonths(-1) }; Show(); }
    void OnNavNext(object sender, RoutedEventArgs e) { state = state with { Selected = state.Selected.AddMonths(1) }; Show(); }

    void OnPicked(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs e)
    {
        if (e.NewDate is not { } date) return;
        state = state with { Selected = DateOnly.FromDateTime(date.Date) };
        Show();
    }

    /// <summary>No calendar glyph on the date pill, as the iPad's compact DatePicker shows none.</summary>
    void OnPickerLoaded(object sender, RoutedEventArgs e)
    {
        if (Find((DependencyObject)sender, "CalendarGlyph") is UIElement glyph) glyph.Visibility = Visibility.Collapsed;
    }

    static DependencyObject? Find(DependencyObject root, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { Name: var n } && n == name) return child;
            if (Find(child, name) is { } found) return found;
        }
        return null;
    }
}
