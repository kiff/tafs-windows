namespace Tafs.Core.Account;

/// <summary>The person's theme, as the iPad's ThemePreference: Light, Dark, or the system's.</summary>
public enum ThemeChoice { System, Light, Dark }

/// <summary>
/// The theme chosen in Account, kept across launches as the iPad keeps <c>tafs.theme</c>: one word (<c>light</c>,
/// <c>dark</c> or <c>system</c>) in a file of its own. Anything it can't read is the system's.
/// </summary>
public static class ThemeChoices
{
    public static string Word(ThemeChoice theme) => theme switch
    {
        ThemeChoice.Light => "light",
        ThemeChoice.Dark => "dark",
        _ => "system",
    };

    public static ThemeChoice Parse(string? word) => word?.Trim() switch
    {
        "light" => ThemeChoice.Light,
        "dark" => ThemeChoice.Dark,
        _ => ThemeChoice.System,
    };

    /// <summary>The theme kept in <paramref name="file"/>; the system's when there's none or it can't be read.</summary>
    public static ThemeChoice Read(string file)
    {
        try { return File.Exists(file) ? Parse(File.ReadAllText(file)) : ThemeChoice.System; }
        catch (IOException) { return ThemeChoice.System; }
        catch (UnauthorizedAccessException) { return ThemeChoice.System; }
    }

    /// <summary>Keeps the theme, whole or not at all: written beside the file, then moved over it.</summary>
    public static void Write(string file, ThemeChoice theme)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var next = file + ".new";
        File.WriteAllText(next, Word(theme));
        File.Move(next, file, overwrite: true);
    }

    /// <summary>
    /// The theme at launch: <c>--theme light|dark</c> when given (CI's screenshots), else the kept one. A screenshot
    /// never reads what this PC kept, so the same command gives the same picture anywhere.
    /// </summary>
    public static ThemeChoice AtLaunch(string? option, bool screenshot, Func<ThemeChoice> kept) =>
        option is "light" or "dark" ? Parse(option) : screenshot ? ThemeChoice.System : kept();
}
