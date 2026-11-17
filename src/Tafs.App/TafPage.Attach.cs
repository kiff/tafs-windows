using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Coach;
using Tafs.Core.Taf;
using Windows.Graphics.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;

namespace Tafs.App;

/// <summary>
/// Photos and files on a message in a taf's chat (P5, kiff/tafs#209, docs/attachments.md), as the iPad's <c>AttachViews</c>:
/// the paperclip picks up to five; each is made ready on this PC (an image redrawn as a JPEG on white, its long edge at most
/// 2,576, so only its pixels go), declared to tafs cloud, and its bytes go straight to storage with no session. The tray
/// shows each going up, ready, or why not, with Remove; Send waits while one is going up, and takes the ready ones.
/// </summary>
public sealed partial class TafPage
{
    /// <summary>A file picked for the next message: its name, and going up, ready (with its record), or why not.</summary>
    sealed class PendingFile(string name)
    {
        public readonly Guid Key = Guid.NewGuid();
        public string Name { get; set; } = name;
        public ChatFile? Ready;
        public string? Why;
        public bool Uploading => Ready is null && Why is null;
    }

    readonly List<PendingFile> pendingFiles = [];

    /// <summary>The ready files' ids, for the message about to go.</summary>
    IReadOnlyList<string> ReadyFiles() => pendingFiles.Where(f => f.Ready is not null).Select(f => f.Ready!.Id).ToList();

    /// <summary>Taken with a message: those files leave the tray; one that failed stays, to remove.</summary>
    void ClearSentFiles(IReadOnlyList<string> sent)
    {
        pendingFiles.RemoveAll(f => f.Ready is { } ready && sent.Contains(ready.Id));
        ShowTray();
    }

    /// <summary>Whether a file is still going up: Send waits, so a message never leaves one just attached behind.</summary>
    bool FilesUploading => pendingFiles.Any(f => f.Uploading);

    async void OnAttach(object sender, RoutedEventArgs e)
    {
        var w = App.Words;
        if (opening is not { CloudId: { } id, Send: { } send }) return;
        var room = FilePrep.Room(pendingFiles.Count);
        if (room == 0) return;
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        foreach (var ext in FilePrep.ByExtension.Keys) picker.FileTypeFilter.Add(ext);
        var picked = await picker.PickMultipleFilesAsync();
        foreach (var file in picked.Take(room))
        {
            var pending = new PendingFile(file.Name);
            pendingFiles.Add(pending);
            ShowTray();
            _ = AttachAsync(pending, file, id, send);
        }
    }

    /// <summary>One file: made ready, declared, then uploaded; the tray says where it stands, or why not.</summary>
    async Task AttachAsync(PendingFile pending, Windows.Storage.StorageFile file, string taf, Func<string, byte[], Task<CloudRead>> send)
    {
        var w = App.Words;
        try
        {
            var (name, mediaType, bytes) = await PrepareAsync(file);
            pending.Name = name;
            if (FilePrep.Problem(w, name, bytes.Length, mediaType) is { } problem) { pending.Why = problem; return; }
            var declared = await send(Attachments.Path(taf), Attachments.DeclareBody(mediaType!, name, bytes.Length, FilePrep.Sha256(bytes)));
            if (declared.Answer is not { } a || Attachments.Declared(a) is not { } d)
            {
                pending.Why = w.Get("chat.file.not_uploaded", ("why", declared.Why ?? w.Get("error.ours")));
                return;
            }
            if (await KiffHttp.UploadAsync(d.Upload, bytes)) pending.Ready = d.File;
            else pending.Why = w.Get("chat.file.not_uploaded", ("why", w.Get("error.file_not_uploaded")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            pending.Why = w.Get("chat.file.unreadable");
        }
        finally
        {
            if (opening?.CloudId == taf) ShowTray();
        }
    }

    /// <summary>
    /// A picked file as it goes: an image redrawn as a JPEG (its long edge at most 2,576, on white, its pixels only, named
    /// .jpg); anything else as it is, by its kind. A kind tafs doesn't take has no media type, and the tray says why.
    /// </summary>
    static async Task<(string Name, string? MediaType, byte[] Bytes)> PrepareAsync(Windows.Storage.StorageFile file)
    {
        if (!FilePrep.IsImage(file.Name))
        {
            var buffer = await Windows.Storage.FileIO.ReadBufferAsync(file);
            return (file.Name, FilePrep.MediaType(file.Name), buffer.ToArray());
        }
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var (width, height) = FilePrep.Scaled(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight);
        var transform = new BitmapTransform { ScaledWidth = width, ScaledHeight = height, InterpolationMode = BitmapInterpolationMode.Fant };
        var pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb)).DetachPixelData();
        // On white: premultiplied, so each channel plus white times what's transparent; then opaque.
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var clear = 255 - pixels[i + 3];
            pixels[i] = (byte)Math.Min(255, pixels[i] + clear);
            pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] + clear);
            pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] + clear);
            pixels[i + 3] = 255;
        }
        using var output = new InMemoryRandomAccessStream();
        var options = new BitmapPropertySet { ["ImageQuality"] = new BitmapTypedValue(0.85, Windows.Foundation.PropertyType.Single) };
        // A new encoder, never one transcoding the original: no location, no camera, no other metadata goes.
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output, options);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, width, height, 96, 96, pixels);
        await encoder.FlushAsync();
        var bytes = new byte[output.Size];
        using (var reader = new DataReader(output.GetInputStreamAt(0)))
        {
            await reader.LoadAsync((uint)output.Size);
            reader.ReadBytes(bytes);
        }
        return (FilePrep.JpegName(App.Words, file.Name), "image/jpeg", bytes);
    }

    /// <summary>The tray: each file with its mark, its name, where it stands (Uploading, Ready, or why in red) and Remove.</summary>
    void ShowTray()
    {
        var w = App.Words;
        FileTray.Children.Clear();
        FileTray.Visibility = pendingFiles.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        AttachButton.IsEnabled = FilePrep.Room(pendingFiles.Count) > 0;
        AutomationProperties.SetName(AttachButton, w.Get("chat.file.attach"));
        foreach (var f in pendingFiles)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new FontIcon { Glyph = FilePrep.IsImage(f.Name) ? "" : "", FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
            var words = new StackPanel { Spacing = 1, MaxWidth = 220 };
            words.Children.Add(new TextBlock { Text = f.Name, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis,
                Style = Look("TafsInkText") });
            words.Children.Add(new TextBlock
            {
                Text = f.Why ?? w.Get(f.Uploading ? "chat.file.uploading" : "chat.file.ready"), FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Style = Look(f.Why is not null ? "TafsNoText" : "TafsDimText"),
            });
            row.Children.Add(words);
            var remove = new Button { Content = new FontIcon { Glyph = "", FontSize = 11 }, Style = Look("TafsArrow"), Width = 28, Height = 28 };
            AutomationProperties.SetName(remove, w.Get("chat.file.remove", ("name", f.Name)));
            var key = f.Key;
            remove.Click += (_, _) => { pendingFiles.RemoveAll(p => p.Key == key); ShowTray(); };
            row.Children.Add(remove);
            FileTray.Children.Add(new Border { Child = row, Padding = new Thickness(10, 6, 6, 6), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
                Background = ChatViews.ThemeBrush(ActualTheme, "TafsPanel"), BorderBrush = ChatViews.ThemeBrush(ActualTheme, "TafsLine") });
        }
        // Send waits while a file is still going up.
        SendButton.IsEnabled = !FilesUploading && !sending;
    }

    /// <summary>
    /// A sent file opened again, as the iPad's Quick Look: tafs cloud's one-minute link, the bytes downloaded with no session
    /// into this PC's temporary folder (removed at sign-out), then opened in Windows' own app for its kind.
    /// </summary>
    async Task OpenFileAsync(ChatFile file)
    {
        if (opening is not { CloudId: { } id, Read: { } read }) return;
        var answer = await read(Attachments.FilePath(id, file.Id));
        if (answer.Answer is not { } a || Attachments.Link(a) is not { } link || await KiffHttp.DownloadAsync(link) is not { } bytes) return;
        var folder = Path.Combine(Path.GetTempPath(), "tafs", "attachments");
        Directory.CreateDirectory(folder);
        // Its own name only, never a path from anywhere.
        var safe = string.Concat(Path.GetFileName(file.Name).Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(folder, file.Id + "-" + (safe.Length > 0 ? safe : "file"));
        await File.WriteAllBytesAsync(path, bytes);
        var stored = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        await Windows.System.Launcher.LaunchFileAsync(stored);
    }
}
