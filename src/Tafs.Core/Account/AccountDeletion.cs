using System.Text.Json;
using Tafs.Core.Kiff;
using Tafs.Core.NeedsYou;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Account;

/// <summary>What deleting the account came to: deleted (the session ends), not (and why), or stopped by a sign-out.</summary>
public enum Deleted { Yes, No, Stopped }

/// <summary>
/// Deleting the account, as the iPad's <c>deleteAccount</c> (App Store guideline 5.1.1(v)): the person confirms
/// with Windows Hello, then tafs cloud erases every taf and what it and kiff/agents keep (<c>POST /v1/account/delete</c>), then
/// planque forgets the account's connections (<c>DELETE /v1/accounts/{tenant}</c>, signed by the device), then KIFF, last,
/// since it ends this PC's session (<c>POST /v1/me/delete</c>, a signed setup call). Each step can be asked again, so when one
/// fails the person tries again and it goes on from there.
/// </summary>
public static class AccountDeletion
{
    public const string CloudPath = "/v1/account/delete";
    public const string KiffPath = "/v1/me/delete";

    public static string PlanquePath(string tenant) => $"/v1/accounts/{CloudRead.PathPart(tenant)}";

    /// <summary>The bodies tafs cloud's and KIFF's deletes take: an empty object.</summary>
    public static byte[] Body() => "{}"u8.ToArray();

    /// <param name="confirm">Windows Hello with the iPad's prompt ("Delete your account").</param>
    /// <param name="cloudDelete">tafs cloud's erase: its answer, <c>{done, failed?}</c>.</param>
    /// <param name="planqueErase">planque's erase, signed by the device: its status.</param>
    /// <param name="kiffDelete">KIFF's delete with its <c>KIFF-Device-Signature</c>: its status.</param>
    /// <param name="signDer">The session's device key over a message.</param>
    public static async Task<(Deleted Done, string? Why)> DeleteAsync(WordBook w, string tenant, Func<string, Task<StepConfirmed>> confirm,
        Func<Task<CloudRead>> cloudDelete, Func<Task<int>> planqueErase, Func<string, Task<int>> kiffDelete,
        Func<byte[], Task<byte[]>> signDer, Func<string> signedAt, Func<bool> current)
    {
        var confirmed = await confirm(w.Get("settings.delete_account.prompt"));
        if (!current()) return (Deleted.Stopped, null);
        // Closed or failing: nothing is deleted, and it says so. ("Cancelled. Nothing changed." replaces this once it's pinned.)
        if (confirmed != StepConfirmed.Yes) return (Deleted.No, w.Get("common.not_sent_unconfirmed"));

        // 1. tafs cloud erases every taf, and what it and kiff/agents keep.
        var erased = await cloudDelete();
        if (!current()) return (Deleted.Stopped, null);
        if (erased.Status == 403) return (Deleted.No, w.Get("settings.delete_account.owner_only"));
        if (erased.Answer is not { } answer)
            return (Deleted.No, w.Get("settings.delete_account.not_erased", ("why", erased.Why ?? w.Get("error.ours"))));
        if (!(answer.ValueKind == JsonValueKind.Object && answer.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True))
            return (Deleted.No, w.Get("settings.delete_account.not_erased_yet"));

        // 2. planque forgets the account's connections, signed by this device.
        int forgot;
        try { forgot = await planqueErase(); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { forgot = 0; }
        if (!current()) return (Deleted.Stopped, null);
        if (forgot is < 200 or > 299)
            return (Deleted.No, w.Get("settings.delete_account.connections_left", ("why", KiffErrors.Words(w, forgot))));

        // 3. KIFF, last: a signed setup call over exactly its request. It ends this PC's session with the account.
        var at = signedAt();
        int deleted;
        try
        {
            var header = KiffSigning.SignatureHeaderV1(at, await signDer(KiffSigning.RequestV1(tenant, "POST", KiffPath, Body(), at)));
            deleted = await kiffDelete(header);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { deleted = 0; }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or System.Runtime.InteropServices.COMException
            or InvalidOperationException or UnauthorizedAccessException)
        {
            return (Deleted.No, w.Get("settings.delete_account.kiff_not_deleted", ("why", w.Get("common.not_sent_unconfirmed"))));
        }
        if (!current()) return (Deleted.Stopped, null);
        if (deleted is < 200 or > 299)
            return (Deleted.No, w.Get("settings.delete_account.kiff_not_deleted", ("why", KiffErrors.Words(w, deleted))));
        return (Deleted.Yes, null);
    }
}
