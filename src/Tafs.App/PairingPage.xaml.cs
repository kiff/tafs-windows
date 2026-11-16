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
    /// <summary>The passkey the QR offers; claimed the moment KIFF answers, so nothing removes a registered one.</summary>
    readonly OfferedPasskey offered = new();
    bool offline;

    /// <summary>KIFF handed over this PC's session. It's kept in memory for now.</summary>
    public static event Action<PairedSession>? Paired;

    public PairingPage()
    {
        InitializeComponent();
        clock.Tick += (_, _) =>
        {
            if (offer is not null && offer.IsExpired(DateTimeOffset.UtcNow))
            {
                Show(PairingStep.Expired);
                // Its passkey was never registered: out of Windows' list. A new code makes another.
                ForgetPasskey();
            }
        };
        Unloaded += (_, _) =>
        {
            clock.Stop();
            cancel.Cancel();
            deviceKey?.Dispose();
            deviceKey = null;
            ForgetPasskey();
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        // "why": the card for a screenshot (--page pair-why), offline as every screenshot is.
        offline = e.Parameter is true or "why";
        // First, why: the card says what the passkey is for, and its button asks Windows Hello. Never a prompt out of nowhere.
        if (e.Parameter is "why" || PairingScreen.AsksWhyFirst(App.Sessions.Kept(), WindowsHello.IsAvailable(), offline))
        {
            ShowWhy();
            return;
        }
        await ShowNewCodeAsync();
    }

    /// <summary>The card before Windows Hello: the illustration, why, and the button. Nothing else of the pairing yet.</summary>
    void ShowWhy()
    {
        var w = App.Words;
        var (title, body, go) = PairingScreen.PasskeyWhy(w);
        Heading.Text = w.Get("pair.title", ("device", PairingScreen.Device(w)));
        Steps.Visibility = CheckCard.Visibility = QrCard.Visibility = NewCode.Visibility = Visibility.Collapsed;
        Busy.IsActive = false;
        Status.Text = "";
        // An already-approved board, unchanged (board 6): no new drawing for this card.
        WhyArt.Child ??= Illustrations.NothingWaiting();
        (WhyTitle.Text, WhyBody.Text, WhyGo.Content) = (title, body, go);
        WhyCard.Visibility = Visibility.Visible;
    }

    /// <summary>The card's button: now Windows Hello asks, and the code follows.</summary>
    async void OnWhyGo(object sender, RoutedEventArgs e)
    {
        WhyCard.Visibility = Visibility.Collapsed;
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
            (offer, var made) = await PairingAttempt.OfferAsync(deviceKey.PublicKeyDer, name, make, offered.Current,
                DateTimeOffset.UtcNow, Features.PairingPasskeyProof, token);
            // A new code that made another passkey leaves the one before unused: out of Windows' list.
            PairingAttempt.Discard(offered.Offer(made), WindowsHello.DeletePlatformCredential);
            if (token.IsCancellationRequested) return;
            await ShowQrAsync();

            var result = await PairingClaim.PollAsync(offer, deviceKey,
                (body, c) => KiffHttp.PostAsync(PairingClaim.Path, body, c), () => DateTimeOffset.UtcNow, Task.Delay, token);
            if (result.Session is { } session)
            {
                // Claimed before anything is awaited: a code expiring, or the page closing, while it's kept removes nothing.
                offered.Claim();
                clock.Stop();
                // Kept before Added shows, so a relaunch reads it instead of pairing the same key again.
                // A passkey session keeps its passkey's credential id, the one the QR offered, so Windows Hello can be asked for it.
                await App.Sessions.KeepAsync(KeptSession.Of(DeviceSignIn.Api, session.AccessToken, session.Tenant, session.Subject,
                    session.DeviceId, session.Mode, session.ExpiresIn, DateTimeOffset.UtcNow, TpmKey.PairingKeyName) with
                {
                    PasskeyId = session.Mode == ApprovalMode.Passkey && offered.Current is { } passkey ? KiffSigning.Base64Url(passkey.CredentialId) : null,
                });
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

    /// <summary>
    /// The passkey the QR offered, removed when no session holds it (<see cref="OfferedPasskey.Release"/>). Gone or not (an older
    /// Windows keeps it), it's no longer offered: a new code makes another. Once KIFF answered, nothing.
    /// </summary>
    void ForgetPasskey()
    {
        if (offline) return;
        PairingAttempt.Discard(offered.Release(), WindowsHello.DeletePlatformCredential);
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
