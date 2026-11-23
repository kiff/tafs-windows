using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Coach;
using Tafs.Core.Taf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Tafs.App;

/// <summary>
/// The files picked for the next message, in a taf's chat or Coach mode (P5, docs/attachments.md), as the
/// iPad's <c>AttachViews</c>: the paperclip picks up to five; each is made ready on this PC (an image redrawn as a JPEG on
/// white, its long edge at most 2,576, so only its pixels go), declared to tafs cloud, and its bytes go straight to storage
/// with no session. The tray shows each going up, ready, or why not, with Remove.
/// </summary>
sealed class AttachTray(StackPanel tray, Button paperclip, Func<FrameworkElement> owner, Action changed)
{
    sealed class Pending(string name)
    {
        public readonly Guid Key = Guid.NewGuid();
        public string Name = name;
        public ChatFile? Ready;
        public string? Why;
        public bool Uploading => Ready is null && Why is null;
    }

    readonly List<Pending> files = [];

    /// <summary>Which taf the files are declared for: a new one empties the tray, as the files are that taf's.</summary>
    string? forTaf;

    /// <summary>The ready files' ids, for the message about to go.</summary>
    public IReadOnlyList<string> Ready => files.Where(f => f.Ready is not null).Select(f => f.Ready!.Id).ToList();

    /// <summary>Whether a file is still going up: Send waits, so a message never leaves one just attached behind.</summary>
    public bool Uploading => files.Any(f => f.Uploading);

    /// <summary>Taken with a message: those files leave the tray; one that failed stays, to remove.</summary>
    public void Sent(IReadOnlyList<string> sent)
    {
        files.RemoveAll(f => f.Ready is { } ready && sent.Contains(ready.Id));
        Show();
    }

    /// <summary>Another taf, another conversation, or a sign-out: the tray empties.</summary>
    public void Clear()
    {
        files.Clear();
        forTaf = null;
        Show();
    }

    /// <summary>
    /// The paperclip, as the iPad's <c>AttachButton</c>: with a camera on this PC, a menu of Take photo and Choose file; with none,
    /// the file picker at once, as before (Take photo only where a camera is, as the iPad's <c>isSourceTypeAvailable(.camera)</c>).
    /// </summary>
    public async Task PickAsync(string taf, Func<string, byte[], Task<CloudRead>> send)
    {
        if (forTaf != taf) { files.Clear(); forTaf = taf; }
        if (FilePrep.Room(files.Count) == 0) return;
        if (!await HasCameraAsync())
        {
            await ChooseAsync(taf, send);
            return;
        }
        var w = App.Words;
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedLeft };
        var take = new MenuFlyoutItem { Text = w.Get("chat.file.take_photo"), Icon = new FontIcon { Glyph = "" } };
        AutomationProperties.SetAutomationId(take, "attach.take-photo");
        take.Click += (_, _) => _ = TakeAsync(taf, send);
        var choose = new MenuFlyoutItem { Text = w.Get("chat.file.choose_file"), Icon = new FontIcon { Glyph = "" } };
        AutomationProperties.SetAutomationId(choose, "attach.choose-file");
        choose.Click += (_, _) => _ = ChooseAsync(taf, send);
        menu.Items.Add(take);
        menu.Items.Add(choose);
        menu.ShowAt(paperclip);
    }

    /// <summary>Whether this PC has a camera now (one can be plugged in or out between two clicks): Windows' video capture devices.</summary>
    static async Task<bool> HasCameraAsync()
    {
        try
        {
            var cameras = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(Windows.Devices.Enumeration.DeviceClass.VideoCapture);
            return cameras.Count > 0;
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Take photo, as the iPad's <c>CameraPicker</c>: Windows' own camera dialog (<c>CameraCaptureUI</c>, with Windows' own
    /// permission prompt), one photo, then the same way as a picked image: redrawn as a JPEG on white, its pixels only, named
    /// "Photo.jpg". Closed, or refused by Windows' camera setting, nothing is added.
    /// </summary>
    async Task TakeAsync(string taf, Func<string, byte[], Task<CloudRead>> send)
    {
        if (forTaf != taf || FilePrep.Room(files.Count) == 0) return;
        Windows.Storage.StorageFile? photo;
        try
        {
            var camera = new Microsoft.Windows.Media.Capture.CameraCaptureUI(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(App.WindowHandle));
            camera.PhotoSettings.Format = Microsoft.Windows.Media.Capture.CameraCaptureUIPhotoFormat.Jpeg;
            camera.PhotoSettings.AllowCropping = false;
            photo = await camera.CaptureFileAsync(Microsoft.Windows.Media.Capture.CameraCaptureUIMode.Photo);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { photo = null; }
        if (photo is null) return;
        try
        {
            // The tray moved to another taf, or filled, while the camera was open: the photo isn't added.
            if (forTaf != taf || FilePrep.Room(files.Count) == 0) return;
            var w = App.Words;
            var pending = new Pending(w.Get("attachment.photo"));
            files.Add(pending);
            Show();
            await AttachAsync(pending, photo, taf, send, named: w.Get("attachment.photo"));
        }
        finally
        {
            // Windows' copy of the photo is a temporary file: gone on every path once it's taken, added or not.
            try { await photo.DeleteAsync(Windows.Storage.StorageDeleteOption.PermanentDelete); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
        }
    }

    /// <summary>Choose file: up to five a message, each declared for <paramref name="taf"/> and sent up as it's picked.</summary>
    async Task ChooseAsync(string taf, Func<string, byte[], Task<CloudRead>> send)
    {
        var room = FilePrep.Room(files.Count);
        if (room == 0) return;
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        foreach (var ext in FilePrep.ByExtension.Keys) picker.FileTypeFilter.Add(ext);
        var picked = await picker.PickMultipleFilesAsync();
        foreach (var file in picked.Take(room))
        {
            var pending = new Pending(file.Name);
            files.Add(pending);
            Show();
            _ = AttachAsync(pending, file, taf, send);
        }
    }

    async Task AttachAsync(Pending pending, Windows.Storage.StorageFile file, string taf, Func<string, byte[], Task<CloudRead>> send, string? named = null)
    {
        var w = App.Words;
        try
        {
            var (name, mediaType, bytes) = await PrepareAsync(file, named);
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
            if (forTaf == taf) Show();
        }
    }

    /// <summary>
    /// A picked file as it goes: an image redrawn as a JPEG (its long edge at most 2,576, on white, its pixels only, by a new
    /// encoder, named .jpg); anything else as it is, by its kind. A kind tafs doesn't take has no media type, and says why.
    /// <paramref name="named"/>, a photo just taken: its name ("Photo"), not the camera's file's.
    /// </summary>
    static async Task<(string Name, string? MediaType, byte[] Bytes)> PrepareAsync(Windows.Storage.StorageFile file, string? named = null)
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
        return (FilePrep.JpegName(App.Words, named ?? file.Name), "image/jpeg", bytes);
    }

    /// <summary>The tray: each file with its mark, its name, where it stands (Uploading, Ready, or why in red) and Remove.</summary>
    public void Show()
    {
        var w = App.Words;
        var theme = owner().ActualTheme;
        tray.Children.Clear();
        tray.Visibility = files.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        paperclip.IsEnabled = FilePrep.Room(files.Count) > 0;
        AutomationProperties.SetName(paperclip, w.Get("chat.file.attach"));
        foreach (var f in files)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new FontIcon { Glyph = FilePrep.IsImage(f.Name) ? "" : "", FontSize = 16, VerticalAlignment = VerticalAlignment.Center,
                Foreground = ChatViews.ThemeBrush(theme, "TafsSoft") });
            var words = new StackPanel { Spacing = 1, MaxWidth = 220 };
            words.Children.Add(new TextBlock { Text = f.Name, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = ChatViews.ThemeBrush(theme, "TafsInk") });
            words.Children.Add(new TextBlock
            {
                Text = f.Why ?? w.Get(f.Uploading ? "chat.file.uploading" : "chat.file.ready"), FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = ChatViews.ThemeBrush(theme, f.Why is not null ? "TafsNo" : "TafsDim"),
            });
            row.Children.Add(words);
            var remove = new Button { Content = new FontIcon { Glyph = "", FontSize = 11 }, Style = (Style)Application.Current.Resources["TafsArrow"], Width = 28, Height = 28 };
            AutomationProperties.SetName(remove, w.Get("chat.file.remove", ("name", f.Name)));
            var key = f.Key;
            remove.Click += (_, _) => { files.RemoveAll(p => p.Key == key); Show(); };
            row.Children.Add(remove);
            tray.Children.Add(new Border { Child = row, Padding = new Thickness(10, 6, 6, 6), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
                Background = ChatViews.ThemeBrush(theme, "TafsPanel"), BorderBrush = ChatViews.ThemeBrush(theme, "TafsLine") });
        }
        changed();
    }

    /// <summary>The folder a sent file is opened from: under the temporary folder, removed at every sign-out.</summary>
    public static string Folder => Path.Combine(Path.GetTempPath(), "tafs", "attachments");

    /// <summary>
    /// A sent file opened again, as the iPad's Quick Look: tafs cloud's one-minute link, the bytes downloaded with no session
    /// into <see cref="Folder"/> under its own name only, then opened in Windows' own app for its kind.
    /// </summary>
    /// <returns>Why it wasn't opened, in the app's words; none once Windows opened it.</returns>
    public static async Task<string?> OpenAsync(string taf, ChatFile file, Func<string, Task<CloudRead>> read)
    {
        var w = App.Words;
        string NotOpened(string why) => w.Get("chat.file.not_opened", ("file", file.Name), ("why", why));
        // Only a kind tafs takes, as its own media type, under a name that stays in the folder: anything else never opens.
        if (Attachments.OpenableName(file) is not { } name) return NotOpened(w.Get("attachment.not_taken"));
        var folder = Path.GetFullPath(Folder);
        var path = Path.GetFullPath(Path.Combine(folder, name));
        if (!path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return NotOpened(w.Get("attachment.not_taken"));
        var answer = await read(Attachments.FilePath(taf, file.Id));
        if (answer.Answer is not { } a || Attachments.Link(a) is not { } link) return NotOpened(answer.Why ?? w.Get("chat.file.could_not_open"));
        if (await KiffHttp.DownloadAsync(link, Attachments.MostDownloaded) is not { } bytes) return NotOpened(w.Get("chat.file.could_not_open"));
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(path, bytes);
        // From the internet, as Windows marks a download: Office opens it in Protected View, and SmartScreen can ask.
        try { await File.WriteAllTextAsync(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException) { }
        var stored = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        return await Windows.System.Launcher.LaunchFileAsync(stored) ? null : NotOpened(w.Get("chat.file.could_not_open"));
    }
}
