using System.Text;
using System.Text.RegularExpressions;

namespace Tafs.Core.Ci;

/// <summary>Path globs on '/'-separated repo paths: * (no '/'), ** (any depth), ? (one character).</summary>
public static class Glob
{
    public static bool IsMatch(string pattern, string path) =>
        ToRegex(pattern).IsMatch(path.Replace('\\', '/'));

    static Regex ToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                var followedBySlash = i + 2 < pattern.Length && pattern[i + 2] == '/';
                sb.Append(followedBySlash ? "(?:.*/)?" : ".*");
                i += followedBySlash ? 2 : 1;
            }
            else if (c == '*') sb.Append("[^/]*");
            else if (c == '?') sb.Append("[^/]");
            else sb.Append(Regex.Escape(c.ToString()));
        }
        return new Regex(sb.Append('$').ToString(), RegexOptions.Compiled);
    }
}
