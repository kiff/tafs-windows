using System.Xml.Linq;

namespace Tafs.Core.Ci;

/// <summary>
/// Keys given twice in one resource dictionary. WinUI's XAML compiler stops on one with no message at all
/// (MSB3073, exit code 1), so CI names them before the build.
/// </summary>
public static class XamlKeys
{
    static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Each key given more than once in the same dictionary, with that dictionary's own key ("Light") or "the file's".</summary>
    public static IReadOnlyList<(string Dictionary, string Key)> Duplicates(string xaml)
    {
        var doc = XDocument.Parse(xaml);
        return doc.Descendants()
            .Where(e => e.Attribute(X + "Key") is not null)
            .GroupBy(e => e.Ancestors().FirstOrDefault(a => a.Name.LocalName == "ResourceDictionary") ?? doc.Root!)
            .SelectMany(d => d.GroupBy(e => e.Attribute(X + "Key")!.Value).Where(g => g.Count() > 1)
                .Select(g => (d.Key.Attribute(X + "Key")?.Value ?? "the file's", g.Key)))
            .ToList();
    }
}
