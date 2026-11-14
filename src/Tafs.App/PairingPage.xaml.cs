using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using QRCoder;
using Tafs.Core.Pairing;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Tafs.App;

/// <summary>
/// Shows this computer's QR and check code, and counts the code down. Nothing goes to the network yet:
/// the claim (PairingClaim.PollAsync) is wired once KIFF serves /v1/pair/claim, and the device key moves to
/// the TPM in its own change. Until then the QR carries a public key with no private half anywhere.
/// </summary>
public sealed partial class PairingPage : Page
{
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    PairingOffer? offer;

    public PairingPage()
    {
        InitializeComponent();
        clock.Tick += (_, _) =>
        {
            if (offer is not null && offer.IsExpired(DateTimeOffset.UtcNow)) Show(PairingStep.Expired);
        };
        Unloaded += (_, _) => clock.Stop();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await ShowNewCodeAsync();

    async void OnNewCode(object sender, RoutedEventArgs e) => await ShowNewCodeAsync();

    async Task ShowNewCodeAsync()
    {
        offer = PairingOffer.Create(
            PairingScreen.KeyWithNoPrivateHalf(), approvalPub: null,
            PairingScreen.DeviceName(App.Words), DateTimeOffset.UtcNow);
        Qr.Source = await QrImageAsync(offer.ToQrJson());
        Show(PairingStep.Waiting);
        clock.Start();
    }

    void Show(PairingStep step, string? why = null)
    {
        var lines = PairingScreen.Lines(App.Words, step, offer!, why);
        Heading.Text = lines.Title;
        Steps.Text = lines.Steps ?? "";
        Steps.Visibility = Shown(lines.Steps);
        CheckLead.Text = lines.CheckLead ?? "";
        CheckCode.Text = lines.CheckCode ?? "";
        CheckCard.Visibility = Shown(lines.CheckCode);
        Status.Text = lines.Status;
        Busy.IsActive = step == PairingStep.Waiting;
        Busy.Visibility = step == PairingStep.Waiting ? Visibility.Visible : Visibility.Collapsed;
        NewCode.Content = lines.NewCode;
        NewCode.Visibility = Shown(lines.NewCode);
        QrCard.Visibility = lines.ShowsQr ? Visibility.Visible : Visibility.Collapsed;
        if (step != PairingStep.Waiting) clock.Stop();
    }

    static Visibility Shown(string? line) => line is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The QR as a PNG, black on white with its quiet zone, medium error correction.</summary>
    static async Task<BitmapImage> QrImageAsync(string text)
    {
        using var data = QRCodeGenerator.GenerateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);
        var image = new BitmapImage();
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(png.AsBuffer());
        stream.Seek(0);
        await image.SetSourceAsync(stream);
        return image;
    }
}
