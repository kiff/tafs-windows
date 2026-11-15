using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.Calendar;

namespace Tafs.App;

/// <summary>
/// Calendar. The tafs' scheduled work comes from tafs cloud (GET /v1/calendar) in its own change: until then
/// every day says it has nothing scheduled.
/// </summary>
public sealed partial class CalendarPage : Page
{
    const double HourHeight = 44;
    DateOnly today;
    CalendarState state = null!;

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
        for (var hour = 0; hour < 24; hour++) Hours.Children.Add(HourRow(hour));
        HoursScroll.Loaded += (_, _) => HoursScroll.ChangeView(null, CalendarScreen.FirstHour * HourHeight, null, true);
        Show();
    }

    void Show()
    {
        var w = App.Words;
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
            var segment = new Button
            {
                Content = title,
                Style = (Style)Application.Current.Resources[mode == state.Mode ? "TafsSegmentChosen" : "TafsSegment"],
            };
            if (mode == state.Mode) AutomationProperties.SetItemStatus(segment, "selected");
            segment.Click += (_, _) => { state = state with { Mode = mode }; Show(); };
            Grid.SetColumn(segment, Modes.Children.Count);
            Modes.Children.Add(segment);
        }

        var week = state.Mode == CalendarMode.Week;
        DayStripScroll.Visibility = Seen(state.Mode == CalendarMode.Day);
        WeekStrip.Visibility = Seen(week);
        HoursScroll.Visibility = Seen(state.Mode == CalendarMode.Day);
        Month.Visibility = Seen(state.Mode == CalendarMode.Month);
        DayList.Visibility = Seen(state.Mode != CalendarMode.Day);

        if (state.Mode == CalendarMode.Day)
        {
            DayStrip.Children.Clear();
            foreach (var day in CalendarScreen.StripDays(state)) DayStrip.Children.Add(Tile(day, fills: false));
            // The selected day's week first, as the iPad scrolls it.
            var at = CalendarScreen.StripDays(state).ToList().IndexOf(CalendarScreen.WeekStart(state.Selected));
            // Once the strip is laid out: before that there's nothing to scroll.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => DayStripScroll.ChangeView(at * (46 + 6), null, null, true));
        }
        else if (week)
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
        else ShowMonth(w);

        DayListHeading.Text = CalendarScreen.DayHeading(w, state.Selected).ToUpper(System.Globalization.CultureInfo.GetCultureInfo(w.Language));
    }

    /// <summary>A day in the strip: its weekday (11 bold) and number (17 semibold) on a panel, corners of 12; the selected one amber, today ringed.</summary>
    Button Tile(DateOnly day, bool fills)
    {
        var chosen = day == state.Selected;
        var lines = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(Text(CalendarScreen.WeekdayName(App.Words, day), 11, Microsoft.UI.Text.FontWeights.Bold, chosen ? "TafsOnAmberText" : "TafsDimText"));
        lines.Children.Add(Text(day.Day.ToString(), 17, Microsoft.UI.Text.FontWeights.SemiBold, chosen ? "TafsOnAmberText" : "TafsInkText"));
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

    /// <summary>The month: the weekdays (11 bold, dim), then six weeks; days of other months dim, the selected one on amber, today ringed.</summary>
    void ShowMonth(Tafs.Core.Words.WordBook w)
    {
        Month.Children.Clear();
        Month.ColumnDefinitions.Clear();
        Month.RowDefinitions.Clear();
        for (var c = 0; c < 7; c++) Month.ColumnDefinitions.Add(new ColumnDefinition());
        for (var r = 0; r < 7; r++) Month.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var grid = CalendarScreen.MonthGrid(state.Selected);
        for (var c = 0; c < 7; c++)
        {
            var name = Text(CalendarScreen.WeekdayName(w, grid[c]), 11, Microsoft.UI.Text.FontWeights.Bold, "TafsDimText");
            name.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(name, c);
            Month.Children.Add(name);
        }
        for (var i = 0; i < grid.Count; i++)
        {
            var day = grid[i];
            var chosen = day == state.Selected;
            var cell = new Button
            {
                Content = Text(day.Day.ToString(), 13, chosen ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                    day.Month == state.Selected.Month ? "TafsInkText" : "TafsDimText"),
                Opacity = day.Month == state.Selected.Month ? 1 : 0.6,
                // Selected and today are separate marks, as on the iPad: the fill, and the ring.
                Style = (Style)Application.Current.Resources["TafsMonth" + (chosen ? "Chosen" : "") + (day == today ? "Today" : chosen ? "" : "Day")],
            };
            AutomationProperties.SetName(cell, CalendarScreen.DayHeading(w, day));
            cell.Click += (_, _) => { state = state with { Selected = day }; Show(); };
            Grid.SetRow(cell, i / 7 + 1);
            Grid.SetColumn(cell, i % 7);
            Month.Children.Add(cell);
        }
    }

    /// <summary>An hour of the day: its line across, and "08:00" at its start, 11 monospaced.</summary>
    static UIElement HourRow(int hour)
    {
        var row = new Border { Height = HourHeight, BorderThickness = new Thickness(0, 1, 0, 0), Style = (Style)Application.Current.Resources["TafsHourLine"] };
        var label = Text($"{hour:00}:00", 11, Microsoft.UI.Text.FontWeights.Normal, "TafsDimText");
        label.HorizontalAlignment = HorizontalAlignment.Left;
        label.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas");
        label.Margin = new Thickness(0, 3, 0, 0);
        row.Child = label;
        return row;
    }

    static TextBlock Text(string text, double size, Windows.UI.Text.FontWeight weight, string style) =>
        new() { Text = text, FontSize = size, FontWeight = weight, HorizontalAlignment = HorizontalAlignment.Center, Style = (Style)Application.Current.Resources[style] };

    static Visibility Seen(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    void OnToday(object sender, RoutedEventArgs e) { if (state.Selected != today) { state = state with { Selected = today }; Show(); } }
    void OnPrevious(object sender, RoutedEventArgs e) { state = CalendarScreen.Step(state, -1); Show(); }
    void OnNext(object sender, RoutedEventArgs e) { state = CalendarScreen.Step(state, 1); Show(); }

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
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { Name: var n } && n == name) return child;
            if (Find(child, name) is { } found) return found;
        }
        return null;
    }
}
