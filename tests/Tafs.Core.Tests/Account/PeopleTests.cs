using System.Text.Json;
using Tafs.Core.Account;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Account;

/// <summary>Account › People &amp; groups, read as the iPad's KIFFClient.Group and Person, and shown as PeopleGroupsView shows them.</summary>
[Part("account")]
public class PeopleTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    static readonly WordBook Fr = WordBook.Load(SharedFiles.Strings, "fr");

    static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    const string TwoGroups = """
        {"groups":[
          {"id":"g_2","name":"support","members":[{"subject":"u_2","role":"member"},{"subject":"u_1","role":"manager"}],"agents":["ag_refunds","ag_gone"]},
          {"id":"g_3","name":"Finance","everyone":false,"members":[],"agents":[]},
          {"id":"g_1","name":"Everyone","everyone":true,"members":[{"subject":"u_1","role":"member"}],"agents":["ag_invoices","ag_refunds"]}]}
        """;

    const string ThreePeople = """
        {"members":[
          {"subject":"u_1","role":"owner","status":"active","email":"rae@acme.test","you":true},
          {"subject":"u_2","role":"member","status":"active","email":null},
          {"subject":"u_3","role":"member","status":"removed","email":"gone@acme.test"}]}
        """;

    static string? TafName(string agent) => agent switch { "ag_refunds" => "Refunds", "ag_invoices" => "Invoices", _ => null };

    /// <summary>KIFF's default group first, then by name lowercased, as refreshPeople sorts them; everyone absent when not said.</summary>
    [Fact]
    public void Groups_read_and_sorted_as_the_iPad_s()
    {
        var groups = People.Groups(Json(TwoGroups))!;
        Assert.Equal(["g_1", "g_3", "g_2"], groups.Select(g => g.Id));
        Assert.Null(groups[2].Everyone);
        Assert.Equal([new GroupMember("u_2", "member"), new GroupMember("u_1", "manager")], groups[2].Members);
        Assert.True(groups[2].Members[1].IsManager);
    }

    /// <summary>Only the people on the account now, by email, else KIFF's id for them.</summary>
    [Fact]
    public void Only_active_people_by_label()
    {
        var people = People.Members(Json(ThreePeople))!;
        Assert.Equal(["rae@acme.test", "u_2"], people.Select(p => p.Label));
        Assert.True(people[0].You);
    }

    /// <summary>A required field missing, or one of the wrong kind, fails the read, as the iPad's decode.</summary>
    [Theory]
    [InlineData("""{"groups":[{"id":"g","name":"x","members":[]}]}""")]
    [InlineData("""{"groups":[{"id":"g","name":"x","members":[{"subject":"u"}],"agents":[]}]}""")]
    [InlineData("""{"groups":[{"id":"g","name":"x","everyone":"yes","members":[],"agents":[]}]}""")]
    [InlineData("""{"groups":[{"id":"g","name":"x","members":[],"agents":[42]}]}""")]
    [InlineData("""{"list":[]}""")]
    public void A_wrong_group_fails_the_read(string json) => Assert.Null(People.Groups(Json(json)));

    [Theory]
    [InlineData("""{"members":[{"subject":"u","role":"member"}]}""")]
    [InlineData("""{"members":[{"subject":"u","role":"member","status":"active","email":3}]}""")]
    [InlineData("""{"members":[{"subject":"u","role":"member","status":"active","you":"no"}]}""")]
    public void A_wrong_person_fails_the_read(string json) => Assert.Null(People.Members(Json(json)));

    /// <summary>The account line: its name and the count, or the count alone when it's named after the email.</summary>
    [Fact]
    public void The_account_line()
    {
        Assert.Equal(("Acme", "rae@acme.test"), People.Account(Json("""{"tenant_id":"t_1","tenant_slug":"acme","tenant_display_name":"Acme","email":"rae@acme.test"}""")));
        Assert.Equal(("acme", null), People.Account(Json("""{"tenant_id":"t_1","tenant_slug":"acme","tenant_display_name":""}""")));
        Assert.Null(People.Account(Json("""{"email":"rae@acme.test"}""")));
        Assert.Null(People.Account(null));
        Assert.Equal("Acme · 2 people", People.AccountLine(En, "Acme", "rae@acme.test", 2));
        Assert.Equal("1 person", People.AccountLine(En, "Rae@Acme.test ", "rae@acme.test", 1));
    }

    /// <summary>What a group uses: only the account's tafs, by name, joined in the app's language.</summary>
    [Fact]
    public void What_a_group_uses()
    {
        var groups = People.Groups(Json(TwoGroups))!;
        Assert.Equal("Uses Invoices and Refunds", People.Uses(En, groups[0], TafName));
        Assert.Equal("Uses no taf yet", People.Uses(En, groups[1], TafName));
        Assert.Equal("Uses Refunds", People.Uses(En, groups[2], TafName));
        Assert.Equal("Everyone on the account · uses no taf", People.Uses(En, groups[0], _ => null));
        Assert.Equal(En.Get("people.everyone_group"), People.ShownName(En, groups[0]));
        Assert.Equal(Fr.Get("people.everyone_group"), People.ShownName(Fr, groups[0]));
        Assert.Equal("Finance", People.ShownName(En, groups[1]));
    }

    /// <summary>Managers first, then by label; the badge is the label's first letter.</summary>
    [Fact]
    public void Managers_first_and_the_badge()
    {
        var groups = People.Groups(Json(TwoGroups))!;
        var people = People.Members(Json(ThreePeople))!;
        var order = People.ManagersFirst(groups[2], s => people.FirstOrDefault(p => p.Subject == s));
        Assert.Equal(["u_1", "u_2"], order.Select(m => m.Subject));
        Assert.Equal("R", People.Initial("rae@acme.test"));
        Assert.Equal("", People.Initial(""));
    }

    /// <summary>GET /v1/me read as strictly as the iPad's KIFFClient.Account (KIFFClient.swift:659-677): what it can't decode draws no account line.</summary>
    [Theory]
    [InlineData("""{"tenant_slug":"acme","email":"rae@acme.test"}""")]
    [InlineData("""{"tenant_id":"t_1","tenant_slug":"acme","email":3}""")]
    [InlineData("""{"tenant_id":"t_1","tenant_slug":"acme","tenant_display_name":7}""")]
    [InlineData("""{"tenant_id":"t_1","tenant_slug":"acme","time_zone":false}""")]
    [InlineData("""{"tenant_id":"t_1","tenant_slug":"acme","capabilities":"cards.manage"}""")]
    [InlineData("""{"tenant_id":1,"tenant_slug":"acme"}""")]
    public void A_me_the_iPad_wouldn_t_decode_has_no_account_line(string json) => Assert.Null(People.Account(Json(json)));
}
