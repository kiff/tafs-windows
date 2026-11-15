using System.Security.Cryptography;
using Tafs.Core.Kiff;
using Tafs.Core.SignIn;

namespace Tafs.App;

/// <summary>
/// This PC's session, kept in the current Windows user's local app data, sealed with Windows' data protection for that
/// user (DPAPI): another Windows user, or the file copied to another PC, can't open it.
/// </summary>
sealed class SessionStore : ISessionStore
{
    static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "tafs");
    static readonly string File = Path.Combine(Folder, "session");
    static readonly byte[] Purpose = "tafs session v1"u8.ToArray();

    public void CheckWritable()
    {
        var probe = Path.Combine(Folder, "probe");
        Directory.CreateDirectory(Folder);
        System.IO.File.WriteAllBytes(probe, ProtectedData.Protect([1], Purpose, DataProtectionScope.CurrentUser));
        System.IO.File.Delete(probe);
    }

    public void Save(byte[] data)
    {
        Directory.CreateDirectory(Folder);
        System.IO.File.WriteAllBytes(File, ProtectedData.Protect(data, Purpose, DataProtectionScope.CurrentUser));
    }

    public byte[]? Load()
    {
        try { return ProtectedData.Unprotect(System.IO.File.ReadAllBytes(File), Purpose, DataProtectionScope.CurrentUser); }
        catch (Exception e) when (e is IOException or CryptographicException or UnauthorizedAccessException) { return null; }
    }

    public void Delete() => System.IO.File.Delete(File);

    /// <summary>The session kept for KIFF's API, if there's one.</summary>
    public KeptSession? Kept() => KeptSessions.Read(Load(), DeviceSignIn.Api, DateTimeOffset.UtcNow);

    /// <summary>
    /// Keeps <paramref name="next"/>, then signs out the session it replaced at KIFF and removes that session's key from
    /// the TPM, as KIFF's steps for moving from view to approver say. Throws when the store refuses: nothing is done then.
    /// </summary>
    public async Task KeepAsync(KeptSession next)
    {
        if (KeptSessions.Replace(this, DeviceSignIn.Api, next, DateTimeOffset.UtcNow) is not { } old) return;
        try
        {
            await KiffHttp.PostAsync(KeptSessions.LogoutPath, "{}"u8.ToArray(), bearer: old.AccessToken);
        }
        catch (HttpRequestException)
        {
            // Offline: the old session ends with its 90 days, or from Devices on the phone.
        }
        try { TpmKey.Delete(old.KeyName); } catch (CryptographicException) { }
    }

    /// <summary>A store that refused, for the screen: Windows' error code only, never a message that could hold a path.</summary>
    public static string Why(Exception e) => $"0x{e.HResult:X8}";
}
