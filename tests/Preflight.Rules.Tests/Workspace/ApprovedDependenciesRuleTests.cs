namespace Preflight.Rules.Tests.Workspace;

using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Rules;
using static Preflight.Rules.Tests.RuleFixture;

/// <summary>
/// Fixes <see cref="ApprovedDependenciesRule"/> and the entry parser it holds.
/// </summary>
/// <remarks>
/// Nothing here reaches a feed, and there is nothing to substitute for one. The
/// question is not whether a package exists but whether somebody agreed to it,
/// and that has an answer offline.
/// </remarks>
public sealed class ApprovedDependenciesRuleTests
{
    private static readonly string[] SerilogAtAnyVersion = ["Serilog@*"];

    private static readonly string[] NoApprovals = [];

    private static readonly string[] EntryWithoutAnAtSign = ["Serilog"];

    private readonly ApprovedDependenciesRule _rule = new();

    private static string Manifest(params string[] dependencies) => $$"""
        {
          "tools": [],
          "dependencies": [{{string.Join(", ", dependencies)}}]
        }
        """;

    private static string Dependency(string id, string? version = "3.1.1") =>
        version is null
            ? $$"""{ "id": "{{id}}" }"""
            : $$"""{ "id": "{{id}}", "version": "{{version}}" }""";

    private static IFileSystem ManifestContaining(string? json)
    {
        var fileSystem = Substitute.For<IFileSystem>();

        // The manifest and nothing else. Answering true for every path would
        // make a restored-marker probe succeed by accident, which is exactly
        // what the dependency rule beside this one asks about.
        fileSystem.FileExists(Arg.Any<string>()).Returns(call =>
            json is not null && call.Arg<string>().EndsWith("preflight.workspace.json", StringComparison.Ordinal));

        if (json is not null)
        {
            fileSystem.ReadAllTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(json);
        }

        return fileSystem;
    }

    private Task<RuleOutcome> Run(string? manifest, string[]? approved) =>
        _rule.ExecuteAsync(
            Context(
                policy: approved is null ? EmptyPolicy() : PolicyWith("approved", approved),
                fileSystem: ManifestContaining(manifest),
                stage: ValidationStage.Workspace),
            CancellationToken.None);

    [Fact]
    public async Task ExecuteAsync_WithNoManifest_IsNotApplicable()
    {
        (await Run(null, SerilogAtAnyVersion)).Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <remarks>
    /// The same file, described the same way as the two rules beside it. Three
    /// rules reading one manifest and giving a syntax error three different
    /// names would leave the reader deciding which to believe.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAMalformedManifest_Fails()
    {
        var outcome = await Run("{ nope", SerilogAtAnyVersion);

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Message.ShouldContain("not valid JSON");
    }

    /// <remarks>
    /// A dependency with no version is still a dependency nobody approved, and
    /// the finding has to say so rather than printing an empty pair of quotes
    /// where the version would go.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ExecuteAsync_ForAnUnapprovedDependencyWithNoVersion_SaysNoVersionWasDeclared(string? version)
    {
        var declared = version is null
            ? Dependency("Newtonsoft.Json", version: null)
            : """{ "id": "Newtonsoft.Json", "version": "" }""";

        var outcome = await Run(Manifest(declared), SerilogAtAnyVersion);

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem().Actual.ShouldNotBeNull().ShouldContain("no version");
    }

    [Fact]
    public async Task ExecuteAsync_WithNoDependencies_IsNotApplicable()
    {
        (await Run("""{ "tools": [] }""", SerilogAtAnyVersion)).Status.ShouldBe(RuleStatus.NotApplicable);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoApprovedList_IsNotApplicable()
    {
        (await Run(Manifest(Dependency("Serilog")), null)).Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <remarks>
    /// An empty list read as "approve nothing" would fail every dependency in
    /// the workspace, and nobody writes an empty array meaning that.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAnEmptyApprovedList_IsNotApplicable()
    {
        (await Run(Manifest(Dependency("Serilog")), NoApprovals)).Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <summary>
    /// The grammar of one entry, whole.
    /// </summary>
    /// <remarks>
    /// The scoped-package row is the one that decides the split: the name
    /// itself begins with an <c>@</c>, so splitting on the first would leave an
    /// entry with no id at all. The wildcard row is the other: it is accepted on
    /// the version and refused on the id, because an id wildcard approves
    /// everything and whoever wrote it meant to write a name.
    /// </remarks>
    [Theory]
    [InlineData("Serilog@3.1.1", "Serilog", "3.1.1", true)]
    [InlineData("Serilog@3.1.1", "Serilog", "3.1.2", false)]
    [InlineData("Serilog@*", "Serilog", "9.9.9", true)]
    [InlineData("Serilog@*", "Serilog", null, true)]
    [InlineData("@scope/pkg@1.0.0", "@scope/pkg", "1.0.0", true)]
    [InlineData("serilog@3.1.1", "SERILOG", "3.1.1", true)]
    [InlineData("*@1.0.0", "Serilog", "1.0.0", false)]
    [InlineData("Serilog@", "Serilog", "3.1.1", false)]
    [InlineData("@1.0.0", "Serilog", "1.0.0", false)]
    [InlineData("Serilog@3.1.1   ", "Serilog", "3.1.1", true)]
    [InlineData("Serilog@3.1.1", "Serilog", "3.1.1.0", false)]
    public async Task ExecuteAsync_MatchesAnApprovedEntry(
        string entry,
        string id,
        string? version,
        bool approved)
    {
        var outcome = await Run(Manifest(Dependency(id, version)), [entry]);

        outcome.Status.ShouldBe(approved ? RuleStatus.Passed : RuleStatus.Failed);
    }

    /// <remarks>
    /// One id listed twice is a list somebody edited without reading, not a
    /// contradiction. Approving it once is enough, and refusing the second
    /// entry would fail a workspace over the shape of the list rather than over
    /// anything in the workspace.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithTheSameEntryListedTwice_StillApproves()
    {
        var outcome = await Run(Manifest(Dependency("Serilog")), ["Serilog@3.1.1", "Serilog@3.1.1"]);

        outcome.Status.ShouldBe(RuleStatus.Passed);
    }

    /// <summary>
    /// The remedy is a conversation, never a command.
    /// </summary>
    /// <remarks>
    /// This is exactly what separates the rule from the one that checks a
    /// dependency has been restored. That one is fixed by running a restore;
    /// this one is fixed by talking to whoever maintains the list, and offering
    /// a command here would send the reader somewhere useless.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithADependencyNotOnTheList_FailsPointingAtTheListOwner()
    {
        var outcome = await Run(Manifest(Dependency("Newtonsoft.Json")), SerilogAtAnyVersion);

        outcome.Status.ShouldBe(RuleStatus.Failed);

        var remediation = outcome.Findings.ShouldHaveSingleItem().Remediation.ShouldNotBeNull();

        remediation.ShouldContain("approved");
        remediation.ShouldNotContain("restore");
    }

    /// <remarks>
    /// A warning is what the report's readers learn to scroll past, which is
    /// the worst place to put a licensing or security decision.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_NeverWarns()
    {
        var outcome = await Run(Manifest(Dependency("Newtonsoft.Json")), SerilogAtAnyVersion);

        outcome.Status.ShouldBe(
            RuleStatus.Failed,
            "Warning is the dependency rule's answer to a different problem, not this rule's.");
    }

    [Fact]
    public async Task ExecuteAsync_WithAnEntryThatHasNoAtSign_FailsNamingTheEntryRatherThanTheDependency()
    {
        var outcome = await Run(Manifest(Dependency("Serilog")), EntryWithoutAnAtSign);

        outcome.Status.ShouldBe(RuleStatus.Failed);

        var finding = outcome.Findings.ShouldHaveSingleItem();

        finding.Message.ShouldContain("approved");
        finding.Actual.ShouldNotBeNull().ShouldContain("Serilog");
        finding.Location.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithOneApprovedAndOneNot_ReportsOnlyTheOne()
    {
        var outcome = await Run(
            Manifest(Dependency("Serilog"), Dependency("Newtonsoft.Json")),
            SerilogAtAnyVersion);

        outcome.Findings.ShouldHaveSingleItem().Message.ShouldContain("Newtonsoft.Json");
    }

    /// <summary>
    /// Two rules about one dependency do not say the same thing twice.
    /// </summary>
    /// <remarks>
    /// They sit next to each other in the same report on the same line of the
    /// manifest, and identical remediations would read as the tool repeating
    /// itself rather than as two independent problems.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_ItsRemediationDiffersFromTheDependenciesRuleAboutTheSameDependency()
    {
        const string Declared = """{ "id": "Newtonsoft.Json", "version": "13.0.3", "restoredMarker": "packages/nj" }""";

        var mine = await Run(Manifest(Declared), SerilogAtAnyVersion);

        var theirs = await new DependenciesRule().ExecuteAsync(
            Context(fileSystem: ManifestContaining(Manifest(Declared)), stage: ValidationStage.Workspace),
            CancellationToken.None);

        mine.Findings.ShouldHaveSingleItem().Remediation
            .ShouldNotBe(theirs.Findings.ShouldHaveSingleItem().Remediation);
    }

    [Fact]
    public async Task ExecuteAsync_WithACancelledToken_StopsRatherThanFinishing()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _rule.ExecuteAsync(
                Context(
                    policy: PolicyWith("approved", SerilogAtAnyVersion),
                    fileSystem: ManifestContaining(Manifest(Dependency("Serilog"))),
                    stage: ValidationStage.Workspace),
                cancellation.Token));
    }
}
