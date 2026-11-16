using Tafs.Core.Connections;
using Tafs.Core.Kiff;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// The account's connections in Connect apps, as the iPad's <c>refreshConnections</c> and <c>disconnect</c>: read on planque as
/// this device, and ended behind Windows Hello with planque's DELETE, signed by this device. None without a session.
/// </summary>
public sealed partial class MainWindow
{
    ConnectionsCalls? ConnectionsFor()
    {
        if (App.Sessions.Kept() is not { } session) return null;
        var reading = sessions;
        var w = App.Words;
        return new ConnectionsCalls(
            Grants: () => GrantsAsync(session),
            // The taf a connection was made for, by its agent in KIFF; none when this PC can't name it (never proof it was removed).
            TafOf: g => home?.Tafs.FirstOrDefault(t => t.Agent == g.Request.Agent && !t.Revoked)?.Name,
            Manages: Halt.ManagesCards(capabilities),
            End: async (ids, prompt) =>
            {
                using var key = TpmKey.Open(session.KeyName);
                async Task<string?> LoadDevice()
                {
                    var devices = await KiffReadAsync(session.AccessToken, PlanqueProof.DevicesPath);
                    var device = devices.Answer is { } answer ? PlanqueProof.ThisDevice(answer) : null;
                    if (reading == sessions) deviceId = device;
                    return device;
                }
                return await AppConnections.EndAsync(w, ids, prompt, ConfirmStepAsync, async id =>
                {
                    var path = AppConnections.DisconnectPath(session.Tenant, id);
                    var signature = await PlanqueProof.SignAsync(session.Tenant, deviceId, LoadDevice, key,
                        () => SigningTime.Next(DateTimeOffset.UtcNow), "DELETE", path, []);
                    return signature is null ? 0 : (await KiffHttp.PlanqueDeleteAsync(new Uri(PlanqueProof.Api, path), signature, w.Language)).Status;
                }, () => reading == sessions);
            });
    }
}
