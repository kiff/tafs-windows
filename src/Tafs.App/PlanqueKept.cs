namespace Tafs.App;

/// <summary>
/// Which account and device this PC set up with planque, as the iPad's <c>planque.enrolled</c> in its Keychain: kept beside the
/// session, so a relaunch doesn't ask for Start again; never a secret, only the account and the device's ids. A new pairing (a
/// new device) or another account isn't set up until it enrols.
/// </summary>
static class PlanqueKept
{
    static readonly string File = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "tafs", "planque");

    static string Line(string tenant, string? device) => tenant + "|" + (device ?? "");

    public static bool Is(string tenant, string? device)
    {
        try { return System.IO.File.Exists(File) && System.IO.File.ReadAllText(File).Trim() == Line(tenant, device); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    public static void Keep(string tenant, string? device)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllText(File, Line(tenant, device));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public static void Forget()
    {
        try { System.IO.File.Delete(File); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
