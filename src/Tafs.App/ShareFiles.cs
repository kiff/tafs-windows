using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Tafs.App;

/// <summary>
/// Windows' share sheet with files, as the iPad's <c>ShareSheet</c> (its <c>UIActivityViewController</c>): shown over the
/// window, offering the apps and devices Windows shares to. The files are read again when Windows asks for them.
/// </summary>
static class ShareFiles
{
    static IReadOnlyList<string> files = [];
    static string title = "";
    static bool listening;

    public static void Show(IReadOnlyList<string> paths, string name)
    {
        files = paths;
        title = name;
        var window = App.WindowHandle;
        if (!listening)
        {
            DataTransferManagerInterop.GetForWindow(window).DataRequested += Requested;
            listening = true;
        }
        DataTransferManagerInterop.ShowShareUIForWindow(window);
    }

    static async void Requested(DataTransferManager sender, DataRequestedEventArgs e)
    {
        var deferral = e.Request.GetDeferral();
        try
        {
            var items = new List<IStorageItem>();
            foreach (var path in files) items.Add(await StorageFile.GetFileFromPathAsync(path));
            e.Request.Data.Properties.Title = title;
            e.Request.Data.SetStorageItems(items);
        }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or IOException)
        {
            // Gone since (a session change removes them): Windows says so in its sheet.
            e.Request.FailWithDisplayText(App.Words.Get("chat.chats_were_not_exported_could", ("device", App.Words.Get("device.windows"))));
        }
        finally
        {
            deferral.Complete();
        }
    }
}
