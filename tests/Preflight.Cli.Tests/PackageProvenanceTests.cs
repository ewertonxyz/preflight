namespace Preflight.Cli.Tests;

using Preflight.Cli.Commands;
using Preflight.Core.Policy;

/// <summary>
/// Fixes how a value that came out of a package is named.
/// </summary>
/// <remarks>
/// Two runs of one commit against two packages must not produce identical
/// output, and a path in a report must not send its reader looking through the
/// checkout for a file that lives somewhere else entirely. See ADR-034.
/// </remarks>
public sealed class PackageProvenanceTests
{
    private static readonly InstalledPipeline Package = Installed("projecta", "1.4.0");

    [Fact]
    public void Qualify_WrapsEveryLeafOriginWithThePackage()
    {
        var document = PackageProvenance.Qualify(
            PolicyDocument.Parse("""{ "schemaVersion": 1 }""", "acme.json"), Package);

        document.Root.TryGetPath(["schemaVersion"], out var node).ShouldBeTrue();

        var origin = ((PolicyNode.Leaf)node!).Value.Origin;

        origin.ShouldBeOfType<PolicyOrigin.FromPackage>();
        ((PolicyOrigin.FromPackage)origin).Pipeline.ShouldBe("projecta");
        ((PolicyOrigin.FromPackage)origin).Version.ShouldBe("1.4.0");
        ((PolicyOrigin.FromPackage)origin).Source.ShouldBeOfType<PolicyOrigin.FromFile>();
    }

    [Fact]
    public void DescribeOrigin_ForAPackageValue_NamesThePackageBeforeTheFile() =>
        InspectionCommandHandlers.DescribeOrigin(
            new PolicyOrigin.FromPackage("projecta", "1.4.0", new PolicyOrigin.FromFile("acme.json", 8)))
            .ShouldBe("projecta@1.4.0/acme.json:8");

    /// <remarks>
    /// The three wrappers nest, and all three have to survive: the file, the
    /// target block that changed the value, and the package the file came from.
    /// </remarks>
    [Fact]
    public void DescribeOrigin_ForAPackageValueInsideATargetBlock_KeepsBoth()
    {
        var described = InspectionCommandHandlers.DescribeOrigin(
            new PolicyOrigin.FromPackage(
                "projecta",
                "1.4.0",
                new PolicyOrigin.FromTarget("switch2", new PolicyOrigin.FromFile("acme.json", 12))));

        described.ShouldContain("projecta@1.4.0/");
        described.ShouldContain("(target switch2)");
    }

    /// <remarks>
    /// Never the absolute install path. It carries the account name of whoever
    /// ran the tool, and these strings reach the NDJSON history and a SARIF
    /// document a review pipeline posts onto a merge request.
    /// </remarks>
    [Fact]
    public void Describe_ForAFileInsideThePackage_IsNameAtVersionAndTheRelativePath() =>
        PackageProvenance.Describe(
            Package, Path.Combine(Package.Root.FullName, "preflight.projecta.json"))
            .ShouldBe("projecta@1.4.0/preflight.projecta.json");

    [Fact]
    public void Describe_NeverLeaksTheInstallPath() =>
        PackageProvenance.Describe(Package, Path.Combine(Package.Root.FullName, "acme.json"))
            .ShouldNotContain(Package.Root.FullName);

    /// <summary>
    /// A path from outside the package is named by its file name alone.
    /// </summary>
    /// <remarks>
    /// The fallback exists because the alternative is worse in both directions:
    /// printing the absolute path leaks the account name into a SARIF posted on
    /// a merge request, and printing nothing loses the only clue the reader has.
    /// It should not be reachable through the resolver — every document
    /// qualified came out of the version directory — which is exactly why it is
    /// asserted here rather than left to be discovered.
    /// </remarks>
    [Fact]
    public void Describe_ForAPathOutsideThePackage_FallsBackToTheFileNameAlone() =>
        PackageProvenance.Describe(Package, Path.Combine(Path.GetTempPath(), "elsewhere", "acme.json"))
            .ShouldBe($"{Package.Name}@{Package.Version}/acme.json");

    /// <remarks>
    /// The walk has a discard arm for the node kinds that carry no value of
    /// their own, and it returns them untouched. Left unexercised, a future node
    /// kind added to the policy model would pass through here silently and lose
    /// its provenance — the values inside it would render as "engine default"
    /// over a package path.
    /// </remarks>
    [Fact]
    public void Qualify_OverADocumentWithNothingInIt_ReturnsItWithThePathQualified()
    {
        var document = PolicyDocument.Parse("""{ }""", "acme.json");

        var qualified = PackageProvenance.Qualify(document, Package);

        qualified.FilePath.ShouldBe($"{Package.Name}@{Package.Version}/acme.json");
        qualified.Root.ShouldBeOfType<PolicyNode.ObjectNode>();
    }

    /// <summary>
    /// The hierarchy the qualifier walks is still exactly two variants.
    /// </summary>
    /// <remarks>
    /// The qualifier tells the two apart with an <c>is</c> and a cast rather
    /// than a switch expression, because a discard over a closed hierarchy is a
    /// permanent hole in the branch count over a line nothing can enter. The
    /// cost of that choice is a third variant reaching the cast and throwing,
    /// and this is the guard that makes it break here — at the moment somebody
    /// widens the hierarchy — instead of in front of a person running
    /// <c>explain</c>.
    /// </remarks>
    [Fact]
    public void ThePolicyNodeHierarchy_IsStillTheTwoVariantsTheQualifierKnows() =>
        typeof(PolicyNode)
            .GetNestedTypes(System.Reflection.BindingFlags.Public)
            .Where(type => type.IsSubclassOf(typeof(PolicyNode)))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBe(["Leaf", "ObjectNode"]);

    [Fact]
    public void EveryEntryPoint_WithoutItsArgument_Throws()
    {
        Should.Throw<ArgumentNullException>(() => PackageProvenance.Describe(null!, "a"));
        Should.Throw<ArgumentNullException>(() => PackageProvenance.Describe(Package, null!));
        Should.Throw<ArgumentNullException>(
            () => PackageProvenance.Qualify(null!, Package));
        Should.Throw<ArgumentNullException>(
            () => PackageProvenance.Qualify(PolicyDocument.Parse("{}", "a.json"), null!));
    }

    private static InstalledPipeline Installed(string name, string version)
    {
        PackageVersion.TryParse(version, out var parsed).ShouldBeTrue();

        return new InstalledPipeline(
            name,
            parsed!,
            new DirectoryInfo(Path.Combine(Path.GetTempPath(), "pf", name, version)),
            PipelineVersionSource.Pin);
    }
}
