using System.Text;
using Tafs.Core.NeedsYou;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.NeedsYou;

/// <summary>Account › Alerts, as the iPad's NotificationSettingsView for this PC's own alerts, and the choices kept for it.</summary>
[Part("account")]
public class AlertPrefsTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    /// <summary>Off until turned on, for each account apart (the iPad's alertsEnabledKey); Sound on until turned off.</summary>
    [Fact]
    public void Alerts_are_off_until_turned_on_for_each_account()
    {
        var prefs = AlertPrefs.Default;
        Assert.False(prefs.IsOn("t_01"));
        Assert.True(prefs.Sound);
        prefs = prefs.With("t_01", true);
        Assert.True(prefs.IsOn("t_01"));
        Assert.False(prefs.IsOn("t_02"));
        prefs = (prefs with { Sound = false }).With("t_01", false);
        Assert.False(prefs.IsOn("t_01"));
        Assert.False(prefs.Sound);
    }

    /// <summary>Kept and read back; anything unreadable is the defaults.</summary>
    [Fact]
    public void The_choices_are_kept()
    {
        var kept = AlertPrefs.Default.With("t_01", true).With("t_02", false) with { Sound = false };
        var read = AlertPrefs.Read(kept.Write());
        Assert.True(read.IsOn("t_01"));
        Assert.False(read.IsOn("t_02"));
        Assert.False(read.Sound);
        Assert.Equal(AlertPrefs.Default, AlertPrefs.Read(null));
        var broken = AlertPrefs.Read(Encoding.UTF8.GetBytes("not json"));
        Assert.Empty(broken.On);
        Assert.True(broken.Sound);
    }

    /// <summary>The iPad's two LockScreenAlerts, and "Alerts on this Windows PC".</summary>
    [Fact]
    public void The_screen_reads_as_the_iPad_s()
    {
        var previews = AlertsScreen.Previews(En);
        Assert.Equal(new AlertPreview("Chargebacks needs you", "Dispute #4825 · €640 is over its limit", true), previews[0]);
        Assert.Equal(new AlertPreview("Refunds answered maria@…", "By email", false), previews[1]);
        Assert.Equal("With sound: Chargebacks needs you. Dispute #4825 · €640 is over its limit", previews[0].Spoken(En));
        Assert.Equal("Silent: Refunds answered maria@…. By email", previews[1].Spoken(En));
        Assert.Equal("Alerts on this Windows PC", AlertsScreen.OnThisDevice(En));
    }
}
