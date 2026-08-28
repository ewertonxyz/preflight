namespace Preflight.Rules.Tests.Workspace;

using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Rules;
using static Preflight.Rules.Tests.RuleFixture;

/// <summary>
/// Fixes <see cref="DependenciesRule"/> and the two degrees it separates.
/// </summary>
/// <remarks>
/// The separation is the point of the rule, and it is the only real producer of
/// <c>RuleStatus.Warning</c> in the built-in set — so it is also what makes
/// <c>PassedWithWarnings</c>, the <c>!</c> glyph and <c>--fail-on-warning</c>
/// demonstrable on something other than a fake rule.
/// </remarks>
public sealed class DependenciesRuleTests
{
    private readonly DependenciesRule _rule = new();

    private static IFileSystem Workspace(string? manifest, params string[] restoredPaths)
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        fileSystem.DirectoryExists(Arg.Any<string>()).Returns(false);

        if (manifest is not null)
        {
            fileSystem.FileExists(Arg.Is<string>(path => path.EndsWith(".json", StringComparison.Ordinal)))
                .Returns(true);
            fileSystem.ReadAllTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(manifest);
        }

        foreach (var restored in restoredPaths)
        {
            fileSystem.FileExists(Arg.Is<string>(path => path.EndsWith(restored, StringComparison.Ordinal)))
                .Returns(true);
        }

        return fileSystem;
    }

    private static string ManifestWith(params string[] dependencies) =>
        $$"""
        { "dependencies": [ {{string.Join(", ", dependencies)}} ] }
        """;

    private static string Dependency(string id, string? version = "1.0.0", string? marker = "packages/marker") =>
        $$"""
        {
          "id": "{{id}}"{{(version is null ? string.Empty : $", \"version\": \"{version}\"")}}{{(marker is null ? string.Empty : $", \"restoredMarker\": \"{marker}\"")}}
        }
        """;

    private Task<RuleOutcome> Run(IFileSystem fileSystem) =>
        _rule.ExecuteAsync(
            Context(fileSystem: fileSystem, stage: ValidationStage.Workspace),
            CancellationToken.None);

    [Fact]
    public async Task ExecuteAsync_WhenEverythingIsDeclaredAndRestored_Passes()
    {
        var outcome = await Run(Workspace(ManifestWith(Dependency("Serilog")), "packages/marker"));

        outcome.Status.ShouldBe(RuleStatus.Passed);
    }

    /// <summary>
    /// Declared, satisfiable, not restored: a warning.
    /// </summary>
    /// <remarks>
    /// Recoverable with one command and nobody's decision, so failing the run
    /// here would fail someone for not having run a restore.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithADependencyThatIsNotRestored_Warns()
    {
        var outcome = await Run(Workspace(ManifestWith(Dependency("Serilog"))));

        outcome.Status.ShouldBe(RuleStatus.Warning);
        outcome.Findings.ShouldHaveSingleItem().Message.ShouldContain("not restored");
    }

    /// <summary>
    /// Declared without a version: a failure.
    /// </summary>
    /// <remarks>
    /// The other arm. No restore can satisfy it, so no command fixes it —
    /// somebody has to decide which version is meant.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithADependencyDeclaredWithoutAVersion_Fails()
    {
        var outcome = await Run(Workspace(ManifestWith(Dependency("Serilog", version: null))));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Message.ShouldContain("without a version");
    }

    /// <summary>
    /// One id at two versions is unsatisfiable, not merely untidy.
    /// </summary>
    /// <remarks>
    /// A restore has to pick one, and which one it picks is exactly the sort of
    /// thing that differs between a developer's machine and the build agent —
    /// the failure this tool exists to catch before the build does.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithOneIdDeclaredAtTwoVersions_FailsNamingBoth()
    {
        var outcome = await Run(Workspace(ManifestWith(
            Dependency("Serilog", "3.1.1"),
            Dependency("Serilog", "4.0.0"))));

        outcome.Status.ShouldBe(RuleStatus.Failed);

        var finding = outcome.Findings.ShouldHaveSingleItem();

        finding.Actual.ShouldNotBeNull().ShouldContain("3.1.1");
        finding.Actual.ShouldNotBeNull().ShouldContain("4.0.0");
    }

    [Fact]
    public async Task ExecuteAsync_WithOneIdDeclaredTwiceAtTheSameVersion_DoesNotComplain()
    {
        var outcome = await Run(Workspace(
            ManifestWith(Dependency("Serilog", "3.1.1"), Dependency("Serilog", "3.1.1")),
            "packages/marker"));

        outcome.Status.ShouldBe(RuleStatus.Passed);
    }

    /// <summary>
    /// A failure hides the warnings, and that is the right order.
    /// </summary>
    /// <remarks>
    /// An unsatisfiable declaration makes every restore question moot: telling
    /// someone to run a restore that cannot succeed sends them round a loop.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithBothProblems_ReportsOnlyTheFailure()
    {
        var outcome = await Run(Workspace(ManifestWith(
            Dependency("Serilog", version: null),
            Dependency("Newtonsoft.Json"))));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldAllBe(finding => !finding.Message.Contains("not restored"));
    }

    /// <remarks>
    /// The rule depends on <c>core.workspace.toolchain</c>, which fails loudly
    /// on the same missing file. Reporting it again would put one problem on
    /// two lines and make the summary count disagree with the number of things
    /// to fix.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithNoManifest_IsNotApplicable()
    {
        (await Run(Workspace(null))).Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WithAManifestDeclaringNoDependencies_IsNotApplicable()
    {
        (await Run(Workspace("""{ "dependencies": [] }"""))).Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WithAMalformedManifest_Fails()
    {
        (await Run(Workspace("{ not json"))).Status.ShouldBe(RuleStatus.Failed);
    }

    /// <remarks>
    /// A dependency with no marker says the manifest has no way to tell whether
    /// it was restored. Inventing a verdict from that would be the rule
    /// asserting something nobody told it.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithADependencyThatDeclaresNoMarker_DoesNotWarn()
    {
        var outcome = await Run(Workspace(ManifestWith(Dependency("Serilog", marker: null))));

        outcome.Status.ShouldBe(RuleStatus.Passed);
    }

    /// <remarks>
    /// A restored dependency is often a directory rather than a file — a
    /// package folder, an extracted archive. Checking only for a file would
    /// warn about every one of them.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_AcceptsADirectoryAsARestoredMarker()
    {
        var fileSystem = Workspace(ManifestWith(Dependency("Serilog")));

        fileSystem.DirectoryExists(Arg.Is<string>(path => path.EndsWith("marker", StringComparison.Ordinal)))
            .Returns(true);

        (await Run(fileSystem)).Status.ShouldBe(RuleStatus.Passed);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsOneWarningPerUnrestoredDependency_InDeclarationOrder()
    {
        var outcome = await Run(Workspace(ManifestWith(
            Dependency("Zeta", marker: "packages/zeta"),
            Dependency("Alpha", marker: "packages/alpha"))));

        outcome.Findings.Select(finding => finding.Location!.RelativePath)
            .ShouldBe(["packages/zeta", "packages/alpha"]);
    }

    [Fact]
    public async Task ExecuteAsync_WhenItWarns_SaysHowToFixIt()
    {
        var outcome = await Run(Workspace(ManifestWith(Dependency("Serilog"))));

        outcome.Findings.ShouldHaveSingleItem().Remediation.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteAsync_WithACancelledToken_StopsRatherThanFinishing()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var many = Enumerable.Range(0, 500)
            .Select(index => Dependency($"Package{index}", marker: $"packages/p{index}"))
            .ToArray();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _rule.ExecuteAsync(
                Context(fileSystem: Workspace(ManifestWith(many)), stage: ValidationStage.Workspace),
                cancellation.Token));
    }
}
