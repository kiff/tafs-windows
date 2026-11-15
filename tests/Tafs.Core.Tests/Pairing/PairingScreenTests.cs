using Tafs.Core.Pairing;
using Tafs.Core.Tests.Kiff;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Pairing;

[Part("pairing")]
public class PairingScreenTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 14, 22, 0, 0, TimeSpan.Zero);
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    public static IEnumerable<object[]> Shipped() => WordBook.Shipped(SharedFiles.Strings).Select(l => new object[] { l });

    static PairingOffer Offer(bool hello = true)
    {
        using var device = new TestSigner();
        using var approval = new TestSigner();
        return PairingOffer.Create(device.PublicKeyDer, hello ? approval.PublicKeyDer : null, "tafs on this Windows PC", Now);
    }

    [Fact]
    public void While_waiting_it_shows_the_QR_the_phone_s_menu_and_the_check_code()
    {
        var offer = Offer();
        var lines = PairingScreen.Lines(En, PairingStep.Waiting, offer);

        Assert.True(lines.ShowsQr);
        Assert.Equal("Add this Windows PC with your phone", lines.Title);
        // The phone's own menu, by the shared words, so a renamed menu item never leaves this test behind.
        Assert.Equal(En.Get("pair.steps", ("account", En.Get("settings.title")), ("devices", En.Get("settings.devices.title")),
            ("pair", En.Get("settings.devices.pair"))), lines.Steps);
        Assert.StartsWith("On your phone, open tafs, then ", lines.Steps);
        Assert.Equal("Your phone asks if your Windows PC shows this code:", lines.CheckLead);
        Assert.Equal(offer.CheckCode(), lines.CheckCode);
        Assert.Equal("Waiting for your phone…", lines.Status);
        Assert.Null(lines.NewCode);
    }

    [Fact]
    public void An_expired_code_hides_the_QR_and_offers_a_new_one()
    {
        var lines = PairingScreen.Lines(En, PairingStep.Expired, Offer());
        Assert.False(lines.ShowsQr);
        Assert.Null(lines.CheckCode);
        Assert.Equal("This code expired.", lines.Status);
        Assert.Equal("Show a new code", lines.NewCode);
    }

    [Fact]
    public void A_failure_says_why_with_KIFF_s_status_only()
    {
        var why = PairingScreen.Why(En, PairingClaim.Read(403, """{"error":"invalid_proof"}"""));
        var lines = PairingScreen.Lines(En, PairingStep.Failed, Offer(), why);
        Assert.Equal("This Windows PC wasn't added: KIFF answered 403. Show a new code and try again.", lines.Status);
        Assert.Equal("Show a new code", lines.NewCode);
    }

    [Fact]
    public void An_unreadable_answer_never_shows_its_body()
    {
        var result = PairingClaim.Read(200, """{"access_token":"secret-token"}""");
        Assert.Equal(PairingStep.Failed, PairingScreen.After(result.Status));
        Assert.Equal("KIFF answered 200", PairingScreen.Why(En, result));
    }

    [Theory]
    [InlineData(ClaimStatus.Pending, PairingStep.Waiting)]
    [InlineData(ClaimStatus.SlowDown, PairingStep.Waiting)]
    [InlineData(ClaimStatus.Expired, PairingStep.Expired)]
    [InlineData(ClaimStatus.Paired, PairingStep.Added)]
    [InlineData(ClaimStatus.InvalidProof, PairingStep.Failed)]
    [InlineData(ClaimStatus.Failed, PairingStep.Failed)]
    public void Each_answer_moves_the_screen(ClaimStatus status, PairingStep step) =>
        Assert.Equal(step, PairingScreen.After(status));

    [Fact]
    public void Added_says_whether_this_computer_approves_itself()
    {
        Assert.Equal("This Windows PC is added.", PairingScreen.Lines(En, PairingStep.Added, Offer(hello: true)).Status);
        Assert.StartsWith("This Windows PC can't sign approvals itself.",
            PairingScreen.Lines(En, PairingStep.Added, Offer(hello: false)).Status);
    }

    [Fact]
    public void The_view_only_notice_names_this_computer()
    {
        var lines = PairingScreen.ViewOnly(En);
        Assert.Equal("View only", lines.Title);
        Assert.Equal("Add this Windows PC", lines.Add);
        Assert.Contains("can't approve until you add it with your phone", lines.Lead);
    }

    [Theory]
    [MemberData(nameof(Shipped))]
    public void Every_line_is_filled_in_every_shipped_language(string language)
    {
        var w = WordBook.Load(SharedFiles.Strings, language);
        var offer = Offer();
        var all = new[] { PairingStep.Waiting, PairingStep.Expired, PairingStep.Failed, PairingStep.Added }
            .Select(step => PairingScreen.Lines(w, step, offer, "x"))
            .SelectMany(l => new[] { l.Title, l.Steps, l.CheckLead, l.Status, l.NewCode })
            .Concat(new[] { PairingScreen.ViewOnly(w).Lead, PairingScreen.ViewOnly(w).Add })
            .OfType<string>();
        Assert.All(all, line => Assert.DoesNotContain("{", line));
    }

    [Theory]
    [MemberData(nameof(Shipped))]
    public void The_name_fits_KIFF_s_limit_in_every_shipped_language(string language)
    {
        var name = PairingScreen.DeviceName(WordBook.Load(SharedFiles.Strings, language));
        Assert.InRange(name.Length, 1, PairingScreen.NameLimit);
        using var device = new TestSigner();
        PairingOffer.Create(device.PublicKeyDer, null, name, Now); // KIFF's checks, as the QR is made
    }

    [Fact]
    public void The_screen_s_key_is_a_real_P256_point_and_new_each_time()
    {
        var keys = Enumerable.Range(0, 20).Select(_ => PairingScreen.KeyWithNoPrivateHalf()).ToList();
        Assert.All(keys, key =>
        {
            using var ec = System.Security.Cryptography.ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(key, out var read); // refuses a point that isn't on the curve
            Assert.Equal(key.Length, read);
            Assert.Equal(key, ec.ExportSubjectPublicKeyInfo());
        });
        Assert.Equal(keys.Count, keys.Select(Convert.ToBase64String).Distinct().Count());
        PairingOffer.Create(keys[0], null, "tafs on this Windows PC", Now);
    }

    [Fact]
    public void The_name_in_English() =>
        Assert.Equal("tafs on this Windows PC", PairingScreen.DeviceName(En));
}
