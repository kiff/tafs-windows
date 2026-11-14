using Xunit.Abstractions;
using Xunit.Sdk;

namespace Tafs.Core.Tests;

/// <summary>
/// Puts a test in a CI part (pairing, approvals, coach, record, connections, calendar, account,
/// strings, contracts, ci). A part runs alone with: dotnet run --project tools/Tafs.Ci -- test approvals
/// </summary>
[TraitDiscoverer("Tafs.Core.Tests.PartDiscoverer", "Tafs.Core.Tests")]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class PartAttribute : Attribute, ITraitAttribute
{
    public PartAttribute(string part) => Part = part;
    public string Part { get; }
}

public sealed class PartDiscoverer : ITraitDiscoverer
{
    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        var part = (string)traitAttribute.GetConstructorArguments().First();
        yield return new KeyValuePair<string, string>("Part", part);
    }
}
