using System.Globalization;
using Tafs.Core.Words;

namespace Tafs.Core.Home;

/// <summary>The app's screens beside the sidebar, apart from a taf.</summary>
public enum Screen { Overview, NeedsYou, Record, Calendar, Usage, ConnectApps, Coach, Account }

/// <summary>One of the app's screens as a sidebar item: its words and its Segoe Fluent icon.</summary>
public sealed record SidebarItem(Screen Screen, string Title, string Glyph);

/// <summary>
/// The iPad's sidebar for the whole app (HomeSplitView): the mascot and "tafs" on top, which open the
/// Overview, so there's no Overview item beside them; the app's screens; Your tafs; Coach and Account at
/// the foot, always in sight.
/// </summary>
public static class Sidebar
{
    /// <summary>The mascot and the app's name: the way to the Overview, and what opens first.</summary>
    public const Screen BrandOpens = Screen.Overview;

    /// <summary>The screens' items, in the iPad's order. Calendar is one of them, so the Overview draws no calendar button.</summary>
    public static IReadOnlyList<SidebarItem> Screens(WordBook w) =>
    [
        new(Screen.NeedsYou, w.Get("needs_you.title"), ""),   // a bell
        new(Screen.Record, w.Get("record.title"), ""),        // a bulleted list
        new(Screen.Calendar, w.Get("calendar.heading"), ""),
        new(Screen.Usage, w.Get("usage.title"), ""),          // a chart
        new(Screen.ConnectApps, w.Get("connect.apps.title"), ""),
    ];

    /// <summary>
    /// Needs you's count at its row's end, as the iPad's sidebar list draws a row's badge: a plain number, none at
    /// zero. The row is then named with it, as the iPad's tab is: "Needs you, 2".
    /// </summary>
    public static (string? Count, string Name) NeedsYouCount(WordBook w, int waiting) =>
        waiting > 0
            ? (waiting.ToString(CultureInfo.GetCultureInfo(w.Language)), w.Get("needs_you.tab_label", ("n", waiting.ToString(CultureInfo.GetCultureInfo(w.Language)))))
            : (null, w.Get("needs_you.title"));

    /// <summary>A section's label, as the iPad's Eyebrow: in capitals, by the language's own rules.</summary>
    public static string Eyebrow(WordBook w, string key) => w.Get(key).ToUpper(CultureInfo.GetCultureInfo(w.Language));

    /// <summary>
    /// Under Your tafs while there are none: tafs cloud's line when it sends one, otherwise the app's own:
    /// connect an app for someone who may use Coach, or where shared tafs will show.
    /// </summary>
    public static string NoTafs(WordBook w, string? cloudWords = null, bool mayUseCoach = true) =>
        cloudWords ?? w.Get(mayUseCoach ? "home.connect_tool_set_up_your" : "home.tafs_shared_you_show");

    /// <summary>Coach and Account at the sidebar's foot, with the account's name under Account once it's known.</summary>
    public static (string Coach, string Account, string AccountLabel) Foot(WordBook w, string? account = null) =>
        (w.Get("coach.title"), w.Get("settings.title"),
         account is null ? w.Get("settings.title") : w.Get("settings.account_named", ("account", account)));
}

/// <summary>The Overview's words: today across the tafs, one line each, each line opening its taf.</summary>
public sealed record OverviewLines(string Title, string Subtitle, IReadOnlyList<string> Columns, string Empty, string Footnote);

public static class OverviewScreen
{
    /// <summary>
    /// The iPad's Overview beside the whole app's sidebar. It has no calendar button: the sidebar already has
    /// Calendar (the iPad's fix of 14 Nov: two ways to it side by side was one too many).
    /// </summary>
    public const bool ShowsCalendar = false;

    public static OverviewLines Lines(WordBook w) => new(
        w.Get("home.overview.title"),
        w.Get("home.overview.subtitle"),
        [w.Get("home.overview.column_taf"), w.Get("home.overview.column_now"),
         w.Get("home.overview.column_card"), w.Get("home.overview.column_last_result")],
        w.Get("home.no_tafs"),
        w.Get("home.overview.each_line_opens"));
}
