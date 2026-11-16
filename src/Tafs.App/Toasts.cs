using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Tafs.Core.NeedsYou;

namespace Tafs.App;

/// <summary>
/// Windows notifications for held calls, through the Windows App SDK (which works for an unpackaged app), as the iPad's
/// hold alerts while tafs runs: one per hold, worded as <see cref="HoldToast"/>. Clicking one opens Needs you at that hold,
/// whether tafs is open or the click starts it. On a PC that won't register them, there are none, and Needs you still
/// shows every hold.
/// </summary>
static class Toasts
{
    static bool on;

    /// <summary>A notification was clicked while tafs runs: the hold it names. Raised off the window's thread.</summary>
    public static event Action<string>? HoldChosen;

    /// <summary>
    /// Registers for notifications, before the window, and answers the hold a click named if that click started tafs. A
    /// screenshot never registers.
    /// </summary>
    public static string? Start()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += (_, args) =>
            {
                if (args.Arguments.TryGetValue(HoldToast.Argument, out var hold)) HoldChosen?.Invoke(hold);
            };
            AppNotificationManager.Default.Register();
            on = true;
        }
        // Windows refused the registration (an old build, or notifications blocked by policy): tafs runs without them.
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            on = false;
        }
        var activated = AppInstance.GetCurrent().GetActivatedEventArgs();
        return activated.Kind == ExtendedActivationKind.AppNotification && activated.Data is AppNotificationActivatedEventArgs click
            && click.Arguments.TryGetValue(HoldToast.Argument, out var started) ? started : null;
    }

    /// <summary>One hold's notification, tagged with its id, so a hold is never shown twice.</summary>
    public static void Show(ToastLines lines)
    {
        if (!on) return;
        var builder = new AppNotificationBuilder()
            .AddArgument(HoldToast.Argument, lines.Hold)
            .AddText(lines.Title);
        // No second line while the PC is locked (HoldToast.Lines).
        if (lines.Body is { } body) builder.AddText(body);
        var notification = builder.BuildNotification();
        notification.Tag = HoldToast.Tag(lines.Hold);
        AppNotificationManager.Default.Show(notification);
    }

    /// <summary>
    /// Whether this PC is locked, so a notification shows only "A taf needs you": the input desktop isn't the person's own
    /// ("Default", it's Winlogon's while locked). When it can't be read, it counts as locked, so the details never reach a
    /// lock screen by chance.
    /// </summary>
    public static bool Locked()
    {
        var desktop = OpenInputDesktop(0, false, DesktopReadObjects);
        if (desktop == IntPtr.Zero) return true;
        try
        {
            var name = new char[64];
            return !GetUserObjectInformationW(desktop, UoiName, name, name.Length * 2, out _)
                || new string(name).TrimEnd('\0') != "Default";
        }
        finally { CloseDesktop(desktop); }
    }

    const uint DesktopReadObjects = 0x0001;
    const int UoiName = 2;
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern bool GetUserObjectInformationW(IntPtr handle, int index, [System.Runtime.InteropServices.Out] char[] info, int length, out int needed);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool CloseDesktop(IntPtr desktop);

    /// <summary>The notifications of holds no longer held, as the iPad's <c>removeAlerts(forHold:)</c>.</summary>
    public static void Remove(IEnumerable<string> holds)
    {
        if (!on) return;
        foreach (var hold in holds) _ = AppNotificationManager.Default.RemoveByTagAsync(HoldToast.Tag(hold));
    }

    /// <summary>Every notification of tafs's, on a sign-out: the last account's calls never stay on a shared PC.</summary>
    public static void RemoveAll()
    {
        if (on) _ = AppNotificationManager.Default.RemoveAllAsync();
    }
}
