namespace Preflight.Core.Tests.Contract;

using System.Reflection;
using System.Xml.Linq;
using static Preflight.TestSupport.RepositoryLayout;

/// <summary>
/// Guards the dependency surface of <c>Preflight.Abstractions</c>.
/// </summary>
/// <remarks>
/// <para>
/// Abstractions depends on nothing beyond the BCL, and this asserts it rather
/// than trusting it. The assertion cost almost nothing to write and has stood
/// since the surface was empty, which is the point: keeping a surface clean is
/// far cheaper than cleaning one.
/// </para>
/// <para>
/// The cost of a dependency here is not paid by this repository. Abstractions
/// is the single assembly an external plugin references in order to write its
/// own rules, so anything added here is inherited by every plugin author, in
/// every project that writes a rule, forever.
/// </para>
/// </remarks>
public sealed class AbstractionsDependencyTests
{
    private const string AbstractionsAssemblyName = "Preflight.Abstractions";

    /// <summary>
    /// Assembly name prefixes that are part of the base class library and are
    /// therefore not dependencies in any meaningful sense.
    /// </summary>
    private static readonly string[] BclPrefixes =
    [
        "System",
        "netstandard",
        "mscorlib",
    ];

    [Fact]
    public void Abstractions_ReferencesNothingOutsideTheBcl()
    {
        var abstractions = Assembly.Load(new AssemblyName(AbstractionsAssemblyName));

        var offenders = abstractions.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Where(name => !BclPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();

        offenders.ShouldBeEmpty(
            "Preflight.Abstractions is referenced by every external plugin. A dependency added " +
            "here is inherited by every project that writes a rule.");
    }

    [Fact]
    public void Abstractions_DoesNotReference_AnyOtherPreflightAssembly()
    {
        var abstractions = Assembly.Load(new AssemblyName(AbstractionsAssemblyName));

        var offenders = abstractions.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Where(name => name.StartsWith("Preflight.", StringComparison.Ordinal))
            .ToArray();

        offenders.ShouldBeEmpty("Abstractions sits at the bottom of the graph and depends on nothing in it.");
    }

    /// <summary>
    /// Closes the gap the two tests above cannot: they read
    /// <see cref="Assembly.GetReferencedAssemblies"/>, which only lists a
    /// reference the compiler actually emitted because some type used it. With
    /// zero types written yet in <c>Preflight.Abstractions</c>, a
    /// <c>PackageReference</c> or <c>ProjectReference</c> added to the csproj
    /// but never consumed by code would be invisible to both — they would keep
    /// passing. This test reads the declaration itself, mirroring
    /// <c>RulesDependencyTests</c> in <c>Preflight.Rules.Tests</c>.
    /// </summary>
    [Fact]
    public void Abstractions_DeclaresNoPackageReferenceOrProjectReference_InTheCsproj()
    {
        var csproj = XDocument.Load(
            PathFromRoot("src", AbstractionsAssemblyName, $"{AbstractionsAssemblyName}.csproj"));

        csproj.Descendants("PackageReference").ShouldBeEmpty();
        csproj.Descendants("ProjectReference").ShouldBeEmpty();
    }
}
