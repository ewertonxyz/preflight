namespace Preflight.Rules.Tests;

using System.Reflection;
using System.Xml.Linq;
using static Preflight.TestSupport.RepositoryLayout;

/// <summary>
/// Guards the dependency rule that makes the plugin model honest.
/// </summary>
/// <remarks>
/// <para>
/// <c>Preflight.Rules</c> consumes <c>Preflight.Abstractions</c> exactly as an
/// external plugin does, with no privileged access to <c>Preflight.Core</c>.
/// </para>
/// <para>
/// This is enforced here rather than left as a convention because the failure
/// mode is silent. If a built-in rule reached into the tool, the gap it
/// worked around in the Abstractions surface would never surface during
/// development — every built-in rule would keep passing. It would be discovered
/// by the first external plugin author, who has no such escape hatch and no way
/// to fix it.
/// </para>
/// <para>
/// The invariant is checked from two angles, because neither alone is enough.
/// The csproj check catches the <em>declaration</em>, which is the reviewable
/// artefact and the thing someone actually edits. The metadata check catches
/// <em>use</em>, and would also catch a reference arriving indirectly, through
/// a package or a transitive project reference that no diff on this csproj
/// would show.
/// </para>
/// </remarks>
public sealed class RulesDependencyTests
{
    private const string RulesAssemblyName = "Preflight.Rules";
    private const string CoreAssemblyName = "Preflight.Core";
    private const string AbstractionsAssemblyName = "Preflight.Abstractions";

    [Fact]
    public void Rules_DeclaresExactlyOneProjectReference_AndItIsAbstractions()
    {
        var csproj = XDocument.Load(
            PathFromRoot("src", RulesAssemblyName, $"{RulesAssemblyName}.csproj"));

        var referencedProjects = csproj.Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .OfType<string>()
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
            .ToArray();

        referencedProjects.ShouldBe(
            [AbstractionsAssemblyName],
            "Preflight.Rules must consume the same contracts an external " +
            "plugin does, and nothing more.");
    }

    [Fact]
    public void Rules_DoesNotUse_Core()
    {
        ReferencedAssemblyNamesOfRules().ShouldNotContain(CoreAssemblyName);
    }

    [Fact]
    public void Rules_DoesNotUse_AnyPreflightAssembly_OtherThanAbstractions()
    {
        var offenders = ReferencedAssemblyNamesOfRules()
            .Where(name => name.StartsWith("Preflight.", StringComparison.Ordinal))
            .Where(name => name != AbstractionsAssemblyName)
            .ToArray();

        offenders.ShouldBeEmpty();
    }

    private static string[] ReferencedAssemblyNamesOfRules()
    {
        // Loaded by name rather than through typeof(SomeRule).Assembly, and by
        // the same name the csproj check above uses. A typeof points at
        // whichever assembly that type currently lives in, so moving a rule
        // would quietly re-aim this guard at a different assembly and it would
        // keep passing. The shared constant is what makes the two halves of the
        // guard incapable of drifting apart.
        var rules = Assembly.Load(new AssemblyName(RulesAssemblyName));

        return [.. rules.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()];
    }
}
