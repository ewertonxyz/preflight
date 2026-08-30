namespace Preflight.Rules.Tests.Samples;

using System.Xml.Linq;
using Sample.Production.Rules;
using static Preflight.TestSupport.RepositoryLayout;

/// <summary>
/// Holds the sample to the dependency rules a real plugin lives under.
/// </summary>
/// <remarks>
/// <para>
/// The mirror of <c>RulesDependencyTests</c>, aimed at a project that is
/// deliberately outside the repository's own assemblies. An external plugin
/// author cannot reach <c>Preflight.Core</c>, and a sample that quietly did
/// would be teaching an escape hatch nobody else has.
/// </para>
/// <para>
/// Checked from both angles, because neither alone is enough. The csproj check
/// catches the <em>declaration</em>, which is the reviewable artefact somebody
/// actually edits; the metadata check catches <em>use</em>, including a
/// reference that arrived through a package or another project and would show
/// up in no diff on this csproj at all.
/// </para>
/// </remarks>
public sealed class SampleDependencyTests
{
    private const string SampleAssemblyName = "Sample.Production.Rules";
    private const string AbstractionsAssemblyName = "Preflight.Abstractions";

    /// <summary>
    /// One project reference, to the contracts, and it is not copied.
    /// </summary>
    /// <remarks>
    /// <c>Private="false"</c> is the line the whole plugin model rests on, and
    /// it is the one a plugin author gets wrong by doing nothing at all: the
    /// default copies <c>Preflight.Abstractions.dll</c> into the output, the
    /// plugin ships its own copy of the contract, and the load context finds it
    /// sitting beside the plugin. The plugin's types then implement a
    /// different <c>IValidationRule</c> from the one the engine is looking for,
    /// and it is rejected as not being a rule at all — while everything about
    /// it looks correct.
    /// </remarks>
    [Fact]
    public void Sample_ReferencesOnlyTheContracts_AndDoesNotCarryThem()
    {
        var csproj = XDocument.Load(
            PathFromRoot("samples", SampleAssemblyName, $"{SampleAssemblyName}.csproj"));

        csproj.Descendants("PackageReference").ShouldBeEmpty();

        var reference = csproj.Descendants("ProjectReference").ShouldHaveSingleItem();

        Path.GetFileNameWithoutExtension(((string)reference.Attribute("Include")!).Replace('\\', '/'))
            .ShouldBe(AbstractionsAssemblyName);

        ((string?)reference.Attribute("Private")).ShouldBe("false");
    }

    [Fact]
    public void Sample_UsesNoPreflightAssemblyOtherThanTheContracts()
    {
        var offenders = typeof(TextureDimensionRule).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Where(name => name.StartsWith("Preflight.", StringComparison.Ordinal))
            .Where(name => name != AbstractionsAssemblyName)
            .ToArray();

        offenders.ShouldBeEmpty();
    }

    // There is deliberately no test asserting that the sample reads through
    // context.FileSystem rather than through System.IO.File.
    //
    // The obvious shapes of one do not work. Scanning local variable types
    // cannot see a static call whose result is a byte array, and proving it
    // properly would mean walking IL. What would be left is an assertion that
    // looks like a guard and is not one — worse than no test, because the next
    // reader trusts it.
    //
    // It is already covered, behaviourally and completely, by
    // SampleTextureDimensionRuleTests: every one of those tests hands the rule
    // an in-memory stream through a substituted IFileSystem and asserts on the
    // dimensions it read back. A sample that reached for the real disk would
    // find nothing at those paths and fail every one of them.
}
