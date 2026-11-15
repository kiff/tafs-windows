using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using QRCoder;
using Tafs.Core.Approvals;
using Tafs.Core.Kiff;
using Tafs.Core.Pairing;
using Tafs.Core.SignIn;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Tafs.App;

/// <summary>
/// Adds this PC with the phone: the device key from the TPM, this PC's own Windows Hello passkey in the QR when
/// Windows makes one, then the claim to KIFF until the phone approves. Navigated to with true (screenshots), it
/// shows a QR whose key has no private half anywhere, and asks nothing of the TPM, Windows Hello or KIFF.
/// </summary>
public sealed partial class PairingPage : Page
{
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    CancellationTokenSource cancel = new();
    PairingOffer? offer;
    TpmKey? deviceKey;
    NewPasskey? passkey;
    bool offline;

    /// <summary>KIFF handed over this PC's session. It's kept in memory for now.</summary>
    public static event Action<PairedSession>? Paired;

    public PairingPage()
    {
        InitializeComponent();
        clock.Tick += (_, _) =>
        {
            if (offer is not null && offer.IsExpired(DateTimeOffset.UtcNow)) Show(PairingStep.Expired);
        };
        Unloaded += (_, _) =>
        {
            clock.Stop();
            cancel.Cancel();
            deviceKey?.Dispose();
            deviceKey = null;
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        offline = e.Parameter is true;
        await ShowNewCodeAsync();
    }

    async void OnNewCode(object sender, RoutedEventArgs e) => await ShowNewCodeAsync();

    async Task ShowNewCodeAsync()
    {
        cancel.Cancel();
        cancel = new CancellationTokenSource();
        var token = cancel.Token;
        NewCode.IsEnabled = false;
        var name = PairingScreen.DeviceName(App.Words);
        try
        {
            if (offline)
            {
                offer = PairingOffer.Create(PairingScreen.KeyWithNoPrivateHalf(), approvalPub: null, name, DateTimeOffset.UtcNow);
                await ShowQrAsync();
                return;
            }

            // Already paired (kept from an earlier launch): never the same key in a new QR, which KIFF would refuse.
            if (PairingScreen.Opening(App.Sessions.Kept()) == PairingStep.Added)
            {
                offer = null;
                Show(PairingStep.Added);
                return;
            }
            // A store that refuses stops the pairing before the code: a session KIFF hands over must be kept.
            App.Sessions.CheckWritable();
            deviceKey ??= TpmKey.OpenOrCreate(TpmKey.PairingKeyName);
            MakePasskey? make = WindowsHello.IsAvailable()
                ? (challenge, c) => new WindowsHello(() => App.WindowHandle)
                    .CreateAsync(PairingAttempt.PasskeyUserId(deviceKey.PublicKeyDer), name, name, challenge, c)
                : null;
            (offer, passkey) = await PairingAttempt.OfferAsync(deviceKey.PublicKeyDer, name, make, passkey,
                DateTimeOffset.UtcNow, Features.PairingPasskeyProof, token);
            if (token.IsCancellationRequested) return;
            await ShowQrAsync();

            var result = await PairingClaim.PollAsync(offer, deviceKey,
                (body, c) => KiffHttp.PostAsync(PairingClaim.Path, body, c), () => DateTimeOffset.UtcNow, Task.Delay, token);
            if (result.Session is { } session)
            {
                // Kept before Added shows, so a relaunch reads it instead of pairing the same key again.
                await App.Sessions.KeepAsync(KeptSession.Of(DeviceSignIn.Api, session.AccessToken, session.Tenant, session.Subject,
                    session.DeviceId, session.Mode, session.ExpiresIn, DateTimeOffset.UtcNow, TpmKey.PairingKeyName));
                Paired?.Invoke(session);
            }
            var step = PairingScreen.After(result.Status);
            Show(step, step == PairingStep.Failed ? PairingScreen.Why(App.Words, result) : null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A new code, or the page closed.
        }
        catch (CryptographicException e)
        {
            Show(PairingStep.Failed, e.Message); // no TPM, or it refused: Windows' own words
        }
        catch (HttpRequestException)
        {
            Show(PairingStep.Offline);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Show(PairingStep.Failed, SessionStore.Why(e)); // the session store refused
        }
        finally
        {
            NewCode.IsEnabled = true;
        }
    }

    async Task ShowQrAsync()
    {
        Qr.Source = await QrImageAsync(offer!.ToQrJson());
        Show(PairingStep.Waiting);
        clock.Start();
    }

    void Show(PairingStep step, string? why = null)
    {
        var lines = PairingScreen.Lines(App.Words, step, offer, why);
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
        if (step != PairingStep.Waiting)
        {
            clock.Stop();
            cancel.Cancel(); // an expired code stops its claim too
        }
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
