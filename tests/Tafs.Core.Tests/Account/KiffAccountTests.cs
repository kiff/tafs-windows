using System.Text.Json;
using Tafs.Core.Account;

namespace Tafs.Core.Tests.Account;

/// <summary>The KIFF account from /v1/me, as the iPad's KIFFClient.Account and AccountLines.</summary>
[Part("account")]
public class KiffAccountTests
{
    static KiffAccount? Read(string json) => KiffAccount.Read(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void The_account_reads_with_its_name_email_and_zone()
    {
        var a = Read("""{"tenant_id":"t_01","tenant_slug":"acme","tenant_display_name":"Acme","email":"rae@acme.test","time_zone":"Europe/Paris","capabilities":["cards.manage"]}""")!;
        Assert.Equal(("t_01", "acme", "Acme", "rae@acme.test", "Europe/Paris"), (a.TenantId, a.Slug, a.Name, a.Email, a.TimeZone));
        Assert.Equal("rae@acme.test", a.SignedInAs);
        Assert.Equal("rae@acme.test", a.SidebarLine);
    }

    /// <summary>No display name reads as the slug; an account named after its email says it once; no email shows the name.</summary>
    [Fact]
    public void Each_thing_is_said_once()
    {
        var solo = Read("""{"tenant_id":"t","tenant_slug":"rae@acme.test","email":"Rae@Acme.test"}""")!;
        Assert.Equal("rae@acme.test", solo.Name);
        Assert.Null(solo.SignedInAs);
        var noEmail = Read("""{"tenant_id":"t","tenant_slug":"acme","tenant_display_name":"  ","email":3}""")!;
        Assert.Equal(("acme", null, "acme"), (noEmail.Name, noEmail.Email, noEmail.SidebarLine));
        Assert.Null(Read("""{"tenant_slug":"acme"}"""));
        Assert.Null(Read("[]"));
    }
}
