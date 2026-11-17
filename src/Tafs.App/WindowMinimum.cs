using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Tafs.Core.Home;
using Windows.Graphics;

namespace Tafs.App;

/// <summary>
/// The window opened near the iPad's landscape proportions and sized to its screen (<see cref="WindowSize"/>), and kept from
/// going under 900 by 600 at the screen's scale, as it's dragged smaller or onto another screen.
/// </summary>
static class WindowMinimum
{
    const uint GetMinMaxInfo = 0x0024;

    /// <summary>Held for the app's life, so the window's hook is never collected.</summary>
    static readonly SubclassProc Hook = OnMessage;

    public static void Open(Window window)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var area = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var (x, y, width, height) = WindowSize.Opening(area.X, area.Y, area.Width, area.Height, scale);
        window.AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        SetWindowSubclass(hwnd, Hook, 1, 0);
    }

    static IntPtr OnMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (message == GetMinMaxInfo)
        {
            var (width, height) = WindowSize.Minimum(GetDpiForWindow(hwnd) / 96.0);
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            info.MinTrackSize = new Point { X = width, Y = height };
            Marshal.StructureToPtr(info, lParam, false);
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct MinMaxInfo { public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }

    delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    [DllImport("comctl32.dll")]
    static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")]
    static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    static extern uint GetDpiForWindow(IntPtr hwnd);
}
