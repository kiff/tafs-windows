using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Tafs.App;

/// <summary>
/// tafs' own start, so one window holds the session: a second start (tafs opened again, or planque's
/// <c>tafs://planque/signed-in</c> return from a vendor's sign-in) hands its activation to the window already running and
/// ends, as Windows App SDK's single-instance guide does. A screenshot (<c>--screenshot</c>) always runs on its own.
/// </summary>
public static partial class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (!args.Contains("--screenshot") && HandedToRunning()) return 0;
        Application.Start(start =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }

    /// <summary>
    /// Whether another tafs is running and took this start: its activation is redirected there on another thread, and
    /// waited for here without blocking COM (CoWaitForMultipleObjects), as the guide requires on the UI thread.
    /// </summary>
    static bool HandedToRunning()
    {
        var running = AppInstance.FindOrRegisterForKey("tafs");
        if (running.IsCurrent) return false;
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var done = CreateEvent(IntPtr.Zero, true, false, null);
        _ = Task.Run(() =>
        {
            try { running.RedirectActivationToAsync(activation).AsTask().Wait(); }
            finally { SetEvent(done); }
        });
        _ = CoWaitForMultipleObjects(0, 0xFFFFFFFF, 1, [done], out _);
        CloseHandle(done);
        return true;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateEventW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateEvent(IntPtr attributes, [MarshalAs(UnmanagedType.Bool)] bool manualReset,
        [MarshalAs(UnmanagedType.Bool)] bool initialState, string? name);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetEvent(IntPtr handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("ole32.dll")]
    private static partial uint CoWaitForMultipleObjects(uint flags, uint timeout, uint count, [In] IntPtr[] handles, out uint index);
}
