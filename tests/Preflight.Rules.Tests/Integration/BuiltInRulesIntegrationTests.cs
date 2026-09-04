namespace Preflight.Rules.Tests.Integration;

using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Core;
using Preflight.Core.Execution;
using Preflight.Rules;
using Preflight.TestSupport;

/// <summary>
/// Runs the six rules against real directories on real disk.
/// </summary>
/// <remarks>
/// <para>
/// The integration layer of the test suite. Every other rule test runs against
/// substituted services, which proves the rules behave as specified — and
/// proves nothing about whether the substitutes were configured to describe
/// reality. A rule that asked <see cref="IFileSystem"/> the wrong question
/// passes all of them.
/// </para>
/// <para>
/// It reaches the shipped <see cref="PhysicalFileSystem"/> and
/// <see cref="ProcessRunner"/>, which lives in <c>Preflight.Core</c>
/// precisely so this layer could exist without a test project referencing an
/// executable.
/// </para>
/// </remarks>
public sealed class BuiltInRulesIntegrationTests
{
    private static readonly PhysicalFileSystem FileSystem = new();
    private static readonly ProcessRunner Processes = new();

    private const string FixtureRoot = "fixtures";

    private static readonly string[] BrokenFixtures = ["toolchain", "dependencies", "build-config", "compile"];

    private static DirectoryInfo Fixture(params string[] segments) =>
        new(RepositoryLayout.PathFromRoot([FixtureRoot, .. segments]));

    private static RuleContext Context(DirectoryInfo root, ValidationStage stage) => new()
    {
        WorkspaceRoot = root,
        Stage = stage,
        Target = new BuildTarget("win64", "Development"),
        ChangedFiles = [],
        Policy = RuleFixture.EmptyPolicy(),
        Logger = Substitute.For<IRuleLogger>(),
        FileSystem = FileSystem,
        Processes = Processes,
    };

    private static Task<RuleOutcome> Run(IValidationRule rule, DirectoryInfo root) =>
        rule.ExecuteAsync(Context(root, rule.Descriptor.Stage), CancellationToken.None);

    /// <summary>
    /// The good fixture satisfies every rule.
    /// </summary>
    /// <remarks>
    /// <c>NotApplicable</c> counts as satisfied here: the two pre-submit rules
    /// get an empty changed-file set, and that is exactly what they should
    /// report. What must not appear is a failure or an error.
    /// </remarks>
    [Fact]
    public async Task EveryRule_AgainstTheGoodWorkspace_PassesOrIsNotApplicable()
    {
        var root = Fixture("workspace-good");

        foreach (var rule in BuiltInRuleDescriptorsTests.Discovered())
        {
            var outcome = await Run(rule, root);

            outcome.Status.ShouldBeOneOf(
                [RuleStatus.Passed, RuleStatus.NotApplicable],
                $"{rule.Descriptor.Id} on the good fixture: {Describe(outcome)}");
        }
    }

    /// <summary>
    /// Each broken fixture breaks exactly one rule.
    /// </summary>
    /// <remarks>
    /// The "exactly" is the assertion. A fixture that also fails a second rule
    /// by accident still makes the intended test green, and the accident stays
    /// invisible until somebody fixes the intended breakage and the fixture
    /// keeps failing.
    /// </remarks>
    [Theory]
    [InlineData("toolchain", "core.workspace.toolchain", RuleStatus.Failed)]
    [InlineData("dependencies", "core.workspace.dependencies", RuleStatus.Warning)]
    [InlineData("build-config", "core.build.configuration", RuleStatus.Failed)]
    [InlineData("compile", "core.build.compile-probe", RuleStatus.Failed)]
    public async Task EachBrokenWorkspace_BreaksExactlyTheIntendedRule(
        string fixture,
        string expectedRuleId,
        RuleStatus expectedStatus)
    {
        var root = Fixture("workspace-broken", fixture);
        var intended = new RuleId(expectedRuleId);

        foreach (var rule in BuiltInRuleDescriptorsTests.Discovered())
        {
            var outcome = await Run(rule, root);

            if (rule.Descriptor.Id == intended)
            {
                outcome.Status.ShouldBe(expectedStatus, $"{intended}: {Describe(outcome)}");

                continue;
            }

            outcome.Status.ShouldNotBe(
                RuleStatus.Errored,
                $"{rule.Descriptor.Id} errored on the '{fixture}' fixture: {Describe(outcome)}");
        }
    }

    /// <summary>
    /// The compile probe leaves the workspace exactly as it found it.
    /// </summary>
    /// <remarks>
    /// The tool never writes to the workspace, and this is the only rule that
    /// can break that: the read-only <see cref="IFileSystem"/> constrains the
    /// rule, not the child process it starts. A compiler told nothing writes
    /// its intermediates next to the sources, and in a real checkout the first
    /// sign would be a diff nobody made.
    /// </remarks>
    [Fact]
    public async Task CompileProbe_LeavesTheFixtureUnchanged()
    {
        var root = Fixture("workspace-broken", "compile");
        var before = Snapshot(root);

        await Run(new CompileProbeRule(), root);

        Snapshot(root).ShouldBe(before);
    }

    /// <summary>
    /// No rule fails without saying how to fix it.
    /// </summary>
    /// <remarks>
    /// A rule that fails without saying how to fix it delivers half the work.
    /// Asserted across every rule and every broken fixture rather than per
    /// rule, because the way this regresses is a new finding added to an
    /// existing rule, not a new rule.
    /// </remarks>
    [Fact]
    public async Task NoRule_ReportsAProblemWithoutARemedy()
    {
        foreach (var fixture in BrokenFixtures)
        {
            var root = Fixture("workspace-broken", fixture);

            foreach (var rule in BuiltInRuleDescriptorsTests.Discovered())
            {
                var outcome = await Run(rule, root);

                outcome.Findings.ShouldAllBe(
                    finding => !string.IsNullOrWhiteSpace(finding.Remediation),
                    $"{rule.Descriptor.Id} on '{fixture}' reported a finding with no remediation.");
            }
        }
    }

    /// <remarks>
    /// Names, sizes and write times. Enough to catch an intermediate written
    /// into the tree, a source file rewritten in place, or a directory created
    /// and left behind.
    /// </remarks>
    private static IReadOnlyList<string> Snapshot(DirectoryInfo root) =>
    [
        .. root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
            .Select(entry => $"{entry.FullName}|{(entry as FileInfo)?.Length}|{entry.LastWriteTimeUtc:O}")
            .Order(StringComparer.Ordinal),
    ];

    private static string Describe(RuleOutcome outcome) =>
        outcome.Findings.Count == 0
            ? "no findings"
            : string.Join(" / ", outcome.Findings.Select(finding => finding.Message));
}
