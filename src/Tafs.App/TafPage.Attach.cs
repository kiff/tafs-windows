using Microsoft.UI.Xaml;
using Tafs.Core.Coach;

namespace Tafs.App;

/// <summary>Photos and files on a message in a taf's chat (P5): the shared <see cref="AttachTray"/>, for this taf.</summary>
public sealed partial class TafPage
{
    AttachTray? attachTray;

    AttachTray Tray => attachTray ??= new AttachTray(FileTray, AttachButton, () => this,
        // Send waits while a file is still going up.
        () => SendButton.IsEnabled = !attachTray!.Uploading && !sending);

    IReadOnlyList<string> ReadyFiles() => Tray.Ready;

    void ClearSentFiles(IReadOnlyList<string> sent) => Tray.Sent(sent);

    bool FilesUploading => Tray.Uploading;

    void ShowTray() => Tray.Show();

    async void OnAttach(object sender, RoutedEventArgs e)
    {
        if (opening is { CloudId: { } id, Send: { } send }) await Tray.PickAsync(id, send);
    }

    async Task OpenFileAsync(ChatFile file)
    {
        if (opening is not { CloudId: { } id, Read: { } read }) return;
        // Not opened: why, under the thread.
        if (await AttachTray.OpenAsync(id, file, read) is { } why)
        {
            (chatNote, chatNoteIsError) = (why, true);
            ShowChat();
        }
    }
}
