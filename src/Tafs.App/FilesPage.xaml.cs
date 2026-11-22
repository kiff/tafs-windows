using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.Files;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.App;

/// <summary>
/// What Files opens on: one taf's (its id at tafs cloud) or the account's (none); tafs cloud's reads and changes as this
/// session; the account's time zone for the days; the file's bytes from the URL tafs cloud gives for a minute.
/// </summary>
public sealed record FilesOpening(string? Taf, Func<string, Task<CloudRead>> Read, Func<HttpMethod, string, byte[]?, Task<CloudRead>>? Change,
    string? TimeZone, Func<Uri, Task<byte[]?>> Fetch);

/// <summary>
/// Files (docs/files.md), as the iPad's <c>FilesView</c> and <c>FileDetailView</c>: the account's files or one taf's, newest first,
/// the list beside the open file at full width (only one of them when narrow, with Back). The account's list groups by day,
/// by taf or by run, with a chip per taf; a taf's by run. A file: its start as plain text (or a picture drawn from its bytes),
/// Download and Share (the file itself, never a link), where it came from, who sees it and when it goes; Delete now apart, at
/// the end, behind a confirm. Its bytes are kept on this PC only while its file is open.
/// </summary>
public sealed partial class FilesPage : Page
{
    enum Grouping { All, ByTaf, ByRun }

    FilesOpening? opening;
    IReadOnlyList<DeliveredFile>? files;
    string? why;
    bool loading;
    Grouping grouping;
    string? chip;
    string? selected;
    /// <summary>The open file's preview, once read; its copy on this PC once Download, Share or a picture asked.</summary>
    PreviewRead? preview;
    string? copy;
    string? note;
    bool narrow;

    public FilesPage()
    {
        InitializeComponent();
        SizeChanged += (_, e) =>
        {
            var now = e.NewSize.Width < 900;
            if (now == narrow) return;
            narrow = now;
            Layout();
        };
    }

    static Style Look(string key) => (Style)Application.Current.Resources[key];

    /// <summary>A theme brush in this page's own theme, as it shows (a dark screenshot or Account › Appearance), for what code draws.</summary>
    Brush Brush(string key) => ChatViews.ThemeBrush(ActualTheme == ElementTheme.Default ? ChatViews.WindowTheme : ActualTheme, key);
    static WordBook W => App.Words;
    static CultureInfo Culture => Locale.Culture(W.Language);
    TimeZoneInfo Zone => opening?.TimeZone is { } id && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var z) ? z : TimeZoneInfo.Local;

    /// <summary>Narrow with a file open: Back closes the file; else back where Files opened from.</summary>
    void OnBack(object sender, RoutedEventArgs e)
    {
        if (narrow && selected is not null) { Close(); return; }
        if (Frame.CanGoBack) Frame.GoBack();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        opening = e.Parameter as FilesOpening;
        BackButton.Content = "‹ " + W.Get("common.back");
        Heading.Text = W.Get("files.title");
        (files, why, selected, chip, grouping) = (null, null, null, null, Grouping.All);
        _ = LoadAsync();
    }

    /// <summary>The copy goes with the screen: never kept on this PC past it.</summary>
    protected override void OnNavigatedFrom(NavigationEventArgs e) => FileCopies.RemoveAll();

    async Task LoadAsync()
    {
        if (opening is not { } o || loading) return;
        loading = true;
        ShowList();
        var (read, failed) = await DeliveredFiles.LoadAsync(o.Read);
        loading = false;
        if (opening != o) return;
        (files, why) = (read, failed is null ? null : W.Get("common.could_not_read_why", ("what", W.Get("files.title")), ("why", failed)));
        ShowList();
        if (!narrow && Shown().FirstOrDefault() is { } first && selected is null) Open(first);
        else Layout();
    }

    IReadOnlyList<DeliveredFile> Shown() => files is null ? [] : DeliveredFiles.Of(files, taf: opening?.Taf ?? chip);

    void Layout()
    {
        var detail = selected is not null;
        ListScroll.Visibility = narrow && detail ? Visibility.Collapsed : Visibility.Visible;
        DetailScroll.Visibility = narrow && !detail ? Visibility.Collapsed : Visibility.Visible;
        ListColumn.Width = narrow ? (detail ? new GridLength(0) : new GridLength(1, GridUnitType.Star)) : new GridLength(380);
        DetailColumn.Width = narrow && !detail ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(DetailScroll, narrow ? 0 : 1);
        if (narrow && detail) Grid.SetColumnSpan(DetailScroll, 2); else Grid.SetColumnSpan(DetailScroll, 1);
    }

    /// <summary>The list, as the iPad's: Loading; why it couldn't be read; nothing yet; else the grouping, the chips, the groups and the kept line.</summary>
    void ShowList()
    {
        List.Children.Clear();
        if (loading && files is null)
        {
            List.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left });
            return;
        }
        if (why is { } failed)
        {
            List.Children.Add(Text(failed, 14, "TafsNoText"));
            var again = new Button { Content = W.Get("common.try_again"), Style = Look("TafsOutlineButton") };
            again.Click += (_, _) => _ = LoadAsync();
            List.Children.Add(again);
            return;
        }
        if (files is not { } all) return;
        if (all.Count == 0 || (opening?.Taf is { } t && DeliveredFiles.Of(all, taf: t).Count == 0))
        {
            List.Children.Add(Text(W.Get("files.empty"), 14, "TafsSoftText"));
            return;
        }
        if (opening?.Taf is null)
        {
            var picker = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var (g, key) in new[] { (Grouping.All, "files.all"), (Grouping.ByTaf, "files.by_taf"), (Grouping.ByRun, "files.by_run") })
                picker.Children.Add(Chip(W.Get(key), grouping == g, () => { grouping = g; ShowList(); }));
            List.Children.Add(picker);
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            chips.Children.Add(Chip(W.Get("files.all_tafs"), chip is null, () => { chip = null; ShowList(); }));
            foreach (var (taf, group) in DeliveredFiles.ByTaf(all))
                chips.Children.Add(Chip(group[0].TafName.Length > 0 ? group[0].TafName : taf, chip == taf, () => { chip = taf; ShowList(); }));
            List.Children.Add(new ScrollViewer { Content = chips, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        }
        var shown = Shown();
        var groups = (opening?.Taf is null ? grouping : Grouping.ByRun) switch
        {
            Grouping.ByTaf => DeliveredFiles.ByTaf(shown).Select(g => (g.Files[0].TafName, g.Files)),
            Grouping.ByRun => DeliveredFiles.ByRun(shown).Select(g => (string.Join(" · ",
                new[] { g.Files[0].TafName, DeliveredFiles.Day(g.Files[0].RunAt, Zone, Culture) }.Where(s => s.Length > 0)), g.Files)),
            _ => DeliveredFiles.ByDay(shown, Zone).Select(g => (DeliveredFiles.Day(g.Files[0].RunAt, Zone, Culture), g.Files)),
        };
        var now = DateTimeOffset.UtcNow;
        foreach (var (header, group) in groups)
        {
            var eyebrow = new TextBlock { Text = header, Style = Look("TafsEyebrow"), Margin = new Thickness(0, 8, 0, 0) };
            AutomationProperties.SetHeadingLevel(eyebrow, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
            List.Children.Add(eyebrow);
            foreach (var file in group) List.Children.Add(Row(file, now));
        }
        List.Children.Add(Text(W.Get("files.kept_line"), 12, "TafsDimText"));
    }

    /// <summary>A file in the list, as the iPad's <c>FileRow</c>: its type's badge, its name, the taf with its size, and when it goes, in amber.</summary>
    Button Row(DeliveredFile file, DateTimeOffset now)
    {
        var lines = new StackPanel { Spacing = 2 };
        lines.Children.Add(Text(file.Name, 15, "TafsInkText", semibold: true));
        lines.Children.Add(Text(string.Join(" · ", new[] { file.TafName, FileSizes.Words(W.Language, file.Bytes) }.Where(s => s.Length > 0)), 13, "TafsDimText"));
        lines.Children.Add(Text(DeliveredFiles.Goes(W, DeliveredFiles.DaysLeft(file, now)), 12, "TafsAmberWords"));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { Badge(file.Type, 40), lines } };
        var button = new Button
        {
            Content = row, Style = Look("TafsQuietButton"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8), CornerRadius = new CornerRadius(10),
            Background = selected == file.Id ? Brush("TafsHover") : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        AutomationProperties.SetName(button, file.Name);
        AutomationProperties.SetAutomationId(button, "file." + file.Id);
        button.Click += (_, _) => Open(file);
        return button;
    }

    /// <summary>The file's type, as a small badge: its own extension, never an icon that says more than it knows.</summary>
    Border Badge(string type, double size) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 4), Background = Brush("TafsSoft"),
        Child = new TextBlock { Text = type, FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = Brush("TafsBackground"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };

    Button Chip(string title, bool on, Action chosen)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = title, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
            Style = Look("TafsQuietButton"), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 6, 12, 6),
            Background = on ? Brush("TafsInk") : Brush("TafsHover"),
            Foreground = on ? Brush("TafsBackground") : Brush("TafsInk"),
        };
        AutomationProperties.SetName(b, title);
        b.Click += (_, _) => chosen();
        return b;
    }

    void Open(DeliveredFile file)
    {
        FileCopies.RemoveAll();
        (selected, preview, copy, note) = (file.Id, null, null, null);
        Layout();
        ShowList();
        ShowDetail();
        _ = ReadPreviewAsync(file);
    }

    void Close()
    {
        FileCopies.RemoveAll();
        (selected, preview, copy, note) = (null, null, null, null);
        Detail.Children.Clear();
        Layout();
        ShowList();
    }

    DeliveredFile? Selected() => files?.FirstOrDefault(f => f.Id == selected);

    async Task ReadPreviewAsync(DeliveredFile file)
    {
        if (opening is not { } o) return;
        var read = await DeliveredFiles.PreviewAsync(file, o.Read, async _ => await BytesAsync(file));
        if (selected != file.Id) return;
        preview = read;
        ShowDetail();
    }

    /// <summary>The file's own bytes: tafs cloud's URL for a minute, read at once; none when either can't be read.</summary>
    async Task<byte[]?> BytesAsync(DeliveredFile file)
    {
        if (opening is not { } o) return null;
        var answer = await o.Read(DeliveredFiles.DownloadPath(file.Id));
        return answer.Answer is { } a && DeliveredFiles.ReadDownload(a) is { } url ? await o.Fetch(url) : null;
    }

    /// <summary>A copy on this PC, for Download and Share only, in its own folder and gone with the screen.</summary>
    async Task<string?> CopyAsync(DeliveredFile file)
    {
        if (copy is not null && File.Exists(copy)) return copy;
        var bytes = preview?.Bytes ?? await BytesAsync(file);
        if (bytes is null) return null;
        copy = FileCopies.Write(file.Id, file.Name, bytes);
        return copy;
    }

    /// <summary>The open file, as the iPad's <c>FileDetailView</c>.</summary>
    void ShowDetail()
    {
        Detail.Children.Clear();
        if (Selected() is not { } file)
        {
            if (files is { Count: > 0 }) Detail.Children.Add(Text(W.Get("files.pick_one"), 14, "TafsSoftText"));
            return;
        }
        var title = Text(file.Name, 20, "TafsInkText", semibold: true);
        title.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetHeadingLevel(title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        Detail.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { Badge(file.Type, 44), title } });
        switch (preview)
        {
            case null:
                Detail.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left });
                break;
            case { Kind: PreviewKind.Text, Start: { } start }:
                // tafs cloud's cleaned start, drawn as plain text: never Markdown, HTML or a viewer of the bytes.
                var text = new TextBlock { Text = start.Text, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13, IsTextSelectionEnabled = true,
                    TextWrapping = TextWrapping.Wrap, Style = Look("TafsInkText"),
                    // The file's own start, as written: each line's direction from its own letters, not the app's language.
                    FlowDirection = FlowDirection.LeftToRight, TextReadingOrder = TextReadingOrder.DetectFromContent };
                AutomationProperties.SetAutomationId(text, "file.preview");
                Detail.Children.Add(new Border { Child = new ScrollViewer { Content = text, MaxHeight = 320 }, Padding = new Thickness(12), CornerRadius = new CornerRadius(10),
                    Background = Brush("TafsPanel"), BorderBrush = Brush("TafsLine"), BorderThickness = new Thickness(1) });
                if (start.Truncated) Detail.Children.Add(Text(W.Get("files.preview_start"), 12, "TafsSoftText"));
                break;
            case { Kind: PreviewKind.Copy, Bytes: { } bytes }:
                // A picture with no text start: drawn on this PC from its own bytes; nothing goes to a viewer service.
                var image = new Image { MaxHeight = 320, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
                _ = Draw(image, bytes);
                AutomationProperties.SetName(image, file.Name);
                Detail.Children.Add(image);
                break;
            case { Kind: PreviewKind.Unreachable }:
                // As the iPad's since tafs#1728: "Files couldn't be read: It didn't reach the server…".
                Detail.Children.Add(Text(W.Get("common.could_not_read_why", ("what", W.Get("files.title")), ("why", W.Get("error.offline"))), 14, "TafsNoText"));
                break;
        }
        var download = new Button { Content = W.Get("files.download"), Style = Look("TafsPrimary"), Padding = new Thickness(18, 8, 18, 8) };
        download.Click += async (_, _) => await DownloadAsync(file);
        var share = new Button { Content = W.Get("files.share"), Style = Look("TafsOutlineButton"), Padding = new Thickness(18, 8, 18, 8) };
        share.Click += async (_, _) =>
        {
            if (await CopyAsync(file) is { } path) ShareFiles.Show([path], file.Name);
            else Say(W.Get("error.offline"));
        };
        Detail.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { download, share } });
        if (note is { } said) Detail.Children.Add(Text(said, 13, "TafsSoftText"));

        var facts = new StackPanel { Spacing = 8 };
        if (file.TafName.Length > 0) facts.Children.Add(Fact(W.Get("files.made_by"), file.TafName));
        facts.Children.Add(Fact(W.Get("files.in_run"), TimeZoneInfo.ConvertTime(file.RunAt, Zone).ToString("d MMM yyyy, HH:mm", Culture)));
        facts.Children.Add(Fact(W.Get("files.type"), file.Type));
        facts.Children.Add(Fact(W.Get("files.size"), FileSizes.Words(W.Language, file.Bytes)));
        facts.Children.Add(Fact(W.Get("files.who_sees"), W.Get("files.everyone")));
        facts.Children.Add(Fact(W.Get("files.goes_away"), W.Get("files.goes_on", ("date", TimeZoneInfo.ConvertTime(file.GoesAt, Zone).ToString("d MMM yyyy", Culture)))));
        Detail.Children.Add(new Border { Child = facts, Padding = new Thickness(16), CornerRadius = new CornerRadius(14), Background = Brush("TafsPanel") });

        if (opening?.Change is not null)
        {
            var delete = new Button { Content = W.Get("files.delete_now"), Style = Look("TafsOutlineButton"), Foreground = Brush("TafsNo") };
            AutomationProperties.SetAutomationId(delete, "file.delete");
            delete.Click += async (_, _) => await DeleteAsync(file);
            Detail.Children.Add(delete);
        }
    }

    static async Task Draw(Image image, byte[] bytes)
    {
        try
        {
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await stream.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(bytes));
            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            image.Source = bitmap;
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or ArgumentException) { image.Visibility = Visibility.Collapsed; }
    }

    static Grid Fact(string label, string value)
    {
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(Text(label, 14, "TafsSoftText"));
        var v = Text(value, 14, "TafsInkText");
        Grid.SetColumn(v, 1);
        g.Children.Add(v);
        return g;
    }

    /// <summary>Download: the file's own bytes, saved where the person picks, as the iPad's <c>fileExporter</c>.</summary>
    async Task DownloadAsync(DeliveredFile file)
    {
        if (await CopyAsync(file) is not { } path)
        {
            Say(W.Get("error.offline"));
            return;
        }
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(file.Name) };
        var ext = Path.GetExtension(file.Name);
        picker.FileTypeChoices.Add(file.Type.Length > 0 ? file.Type : W.Get("files.title"), [ext.Length > 0 ? ext : "."]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        if (await picker.PickSaveFileAsync() is not { } target) return;
        try
        {
            await File.WriteAllBytesAsync(target.Path, await File.ReadAllBytesAsync(path));
            Say(W.Get("files.saved"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Say(e.Message); }
    }

    /// <summary>Delete now, for everyone, behind its confirm: gone from the list only once tafs cloud deleted it.</summary>
    async Task DeleteAsync(DeliveredFile file)
    {
        if (opening?.Change is not { } change) return;
        var ask = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = W.Get("files.delete_question", ("name", file.Name)), Content = W.Get("files.delete_body"),
            PrimaryButtonText = W.Get("files.delete_now"), CloseButtonText = W.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ActualTheme,
        };
        if (await ask.ShowAsync() != ContentDialogResult.Primary) return;
        var answer = await change(HttpMethod.Delete, DeliveredFiles.FilePath(file.Id), null);
        if (answer.Answer is null)
        {
            Say(W.Get("error.offline"));
            return;
        }
        files = files?.Where(f => f.Id != file.Id).ToList();
        Close();
        if (!narrow && Shown().FirstOrDefault() is { } next) Open(next);
    }

    void Say(string said)
    {
        note = said;
        ShowDetail();
    }

    static TextBlock Text(string text, double size, string style, bool semibold = false) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Style = Look(style),
        FontWeight = semibold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
    };
}

/// <summary>
/// The copies of files this PC holds while one is open, as the iPad's <c>FileCopy</c>: each in its own folder under the app's
/// temporary folder, named as the file is (never a path), and gone when its screen closes, on sign-out and on an account switch.
/// </summary>
static class FileCopies
{
    public static string Root => Path.Combine(Path.GetTempPath(), "tafs", "files");

    public static string? Write(string id, string name, byte[] bytes)
    {
        if (FileText.CopyPath(Root, id, name) is not { } path) return null;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public static void RemoveAll() => Tafs.Core.Account.ChatsExport.Remove(Root);
}
