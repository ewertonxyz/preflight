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
/// Runs the twelve rules against real directories on real disk.
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
/// It reaches the shipped <see cref="PhysicalFileSystem"/>,
/// <see cref="ProcessRunner"/> and <see cref="PhysicalVolumeProbe"/>, which
/// live in <c>Preflight.Core</c> precisely so this layer could exist without a
/// test project referencing an executable.
/// </para>
/// </remarks>
public sealed class BuiltInRulesIntegrationTests
{
    private static readonly PhysicalFileSystem FileSystem = new();
    private static readonly ProcessRunner Processes = new();
    private static readonly PhysicalVolumeProbe Volumes = new();

    private const string FixtureRoot = "fixtures";

    private static readonly string[] BrokenFixtures =
    [
        "toolchain",
        "dependencies",
        "build-config",
        "compile",
        "lfs-pointer",
        "companion-file",
        "approved-dependencies",
        "platform-sdk",
    ];

    /// <summary>
    /// One broken fixture, and everything needed to run the rules against it.
    /// </summary>
    /// <remarks>
    /// Policy and change set belong here rather than in the loop, because half
    /// of the twelve rules check nothing at all until somebody configures them
    /// — and a fixture arranged without its policy makes the rule it was built
    /// to break report that it looked at nothing, which reads as success.
    /// </remarks>
    private sealed record BrokenCase(
        string Directory,
        string RuleId,
        RuleStatus Status,
        IPolicyReader Policy,
        IReadOnlyList<ChangedFile> ChangedFiles);

    /// <summary>
    /// The names of the broken fixtures, as strings.
    /// </summary>
    /// <remarks>
    /// Strings rather than the cases themselves so that xUnit serialises them
    /// and a failing row names the directory it was about.
    /// </remarks>
    public static TheoryData<string> BrokenCases() => [.. BrokenFixtures];

    private static BrokenCase CaseFor(string name) => name switch
    {
        "toolchain" => new BrokenCase(name, "core.workspace.toolchain", RuleStatus.Failed, Configured(), []),
        "dependencies" => new BrokenCase(name, "core.workspace.dependencies", RuleStatus.Warning, Configured(), []),
        "build-config" => new BrokenCase(name, "core.build.configuration", RuleStatus.Failed, Configured(), []),
        "compile" => new BrokenCase(name, "core.build.compile-probe", RuleStatus.Failed, Configured(), []),

        "lfs-pointer" => new BrokenCase(
            name,
            "core.presubmit.lfs-pointer",
            RuleStatus.Failed,
            Configured(),
            [RuleFixture.Added("art/hero.psd")]),

        "companion-file" => new BrokenCase(
            name,
            "core.presubmit.companion-file",
            RuleStatus.Failed,
            Configured(),
            [RuleFixture.Added("Art/hero.png")]),

        "approved-dependencies" => new BrokenCase(
            name,
            "core.workspace.approved-dependencies",
            RuleStatus.Failed,
            Configured(),
            []),

        // An impossible floor rather than a command nobody has. Naming an
        // executable that does not exist would pass on the first machine where
        // somebody happens to have a binary by that name.
        _ => new BrokenCase(
            name,
            "core.build.platform-sdk",
            RuleStatus.Failed,
            Configured(sdkMinimum: "999.0.0"),
            []),
    };

    /// <summary>
    /// The change set the good fixture is validated with.
    /// </summary>
    private static IReadOnlyList<ChangedFile> GoodChanges() =>
    [
        RuleFixture.Added("art/hero.psd"),
        RuleFixture.Added("art/hero.png"),
        RuleFixture.Added("art/hero.png.meta"),
        RuleFixture.Added("src/Program.cs"),
    ];

    private static readonly string[] PngMetaPair = ["**/*.png -> {dir}/{name}.{ext}.meta"];

    private static readonly string[] SerilogApproved = ["Serilog@3.1.1"];

    private static readonly string[] VersionArgument = ["--version"];

    /// <summary>
    /// The settings that switch on the rules which check nothing by default.
    /// </summary>
    /// <remarks>
    /// Every rule reads only its own keys, so one reader carrying all of them is
    /// what a real policy resolves to for each rule in turn — and it keeps the
    /// arrangement in one place rather than one per rule.
    /// </remarks>
    private static IPolicyReader Configured(string sdkMinimum = "2.0.0") =>
        RuleFixture.PolicyWith(new Dictionary<string, object?>
        {
            ["attributesPath"] = "lfs.gitattributes",
            ["pairs"] = PngMetaPair,
            ["approved"] = SerilogApproved,
            ["sdk.command"] = "git",
            ["sdk.name"] = "git",
            ["sdk.arguments"] = VersionArgument,
            ["sdk.minimumVersion"] = sdkMinimum,
        });

    private static DirectoryInfo Fixture(params string[] segments) =>
        new(RepositoryLayout.PathFromRoot([FixtureRoot, .. segments]));

    private static RuleContext Context(
        DirectoryInfo root,
        ValidationStage stage,
        IPolicyReader? policy = null,
        IReadOnlyList<ChangedFile>? changed = null) => new()
        {
            WorkspaceRoot = root,
            Stage = stage,
            Target = new BuildTarget("win64", "Development"),
            ChangedFiles = changed ?? [],
            Policy = policy ?? RuleFixture.EmptyPolicy(),
            Logger = Substitute.For<IRuleLogger>(),
            FileSystem = FileSystem,
            Processes = Processes,

            // The shipped probe, not a substitute. Leaving it out compiles
            // perfectly and makes the free-space rule report that it checked
            // nothing on every fixture, which is exactly the silence this layer
            // exists to break.
            Volumes = Volumes,
        };

    private static Task<RuleOutcome> Run(
        IValidationRule rule,
        DirectoryInfo root,
        IPolicyReader? policy = null,
        IReadOnlyList<ChangedFile>? changed = null) =>
        rule.ExecuteAsync(Context(root, rule.Descriptor.Stage, policy, changed), CancellationToken.None);

    /// <summary>
    /// Every rule reaches its own positive path against the good fixture.
    /// </summary>
    /// <remarks>
    /// <c>NotApplicable</c> fails this test, and that is the whole point of it.
    /// Half of the twelve check nothing until they are configured, so a run that
    /// accepted n/a would be green having exercised none of them — which is what
    /// the companion test below, on its own, would have allowed.
    /// </remarks>
    [Fact]
    public async Task EveryRule_AgainstTheGoodWorkspace_ReachesItsPositivePath()
    {
        var root = Fixture("workspace-good");

        foreach (var rule in BuiltInRuleDescriptorsTests.Discovered())
        {
            var outcome = await Run(rule, root, Configured(), GoodChanges());

            outcome.Status.ShouldBe(
                RuleStatus.Passed,
                $"{rule.Descriptor.Id} on the good fixture: {Describe(outcome)}");
        }
    }

    /// <summary>
    /// The good fixture satisfies every rule even when nothing is configured.
    /// </summary>
    /// <remarks>
    /// Kept beside the test above rather than replaced by it. This one is the
    /// guard against a rule being <em>wrong</em> on an unconfigured workspace —
    /// which is the state most repositories are in — and the other cannot see
    /// that, because it hands every rule the configuration it wanted.
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
    /// Each broken fixture breaks the rule it was built for, and errors none of
    /// the others.
    /// </summary>
    /// <remarks>
    /// The summary used to say "exactly one", and the assertion has never said
    /// that: it says the intended rule reaches the intended status and that no
    /// other rule <em>errored</em>. Another rule failing for a second reason
    /// still passes, and the build-configuration fixture does exactly that —
    /// it carries no manifest, so the toolchain rule fails on it too. Making
    /// "exactly one" true would mean every broken fixture had to be a good
    /// workspace in all other respects, which is a larger change than this;
    /// what is written here is what is checked.
    /// </remarks>
    [Theory]
    [MemberData(nameof(BrokenCases))]
    public async Task EachBrokenWorkspace_BreaksTheIntendedRuleAndErrorsNoOther(string fixture)
    {
        var broken = CaseFor(fixture);
        var root = Fixture("workspace-broken", broken.Directory);
        var intended = new RuleId(broken.RuleId);

        foreach (var rule in BuiltInRuleDescriptorsTests.Discovered())
        {
            var outcome = await Run(rule, root, broken.Policy, broken.ChangedFiles);

            if (rule.Descriptor.Id == intended)
            {
                outcome.Status.ShouldBe(broken.Status, $"{intended}: {Describe(outcome)}");

                continue;
            }

            outcome.Status.ShouldNotBe(
                RuleStatus.Errored,
                $"{rule.Descriptor.Id} errored on the '{fixture}' fixture: {Describe(outcome)}");
        }
    }

    /// <summary>
    /// No rule leaves the workspace different from how it found it.
    /// </summary>
    /// <remarks>
    /// The read-only <see cref="IFileSystem"/> constrains a rule, and constrains
    /// neither the child process a rule starts nor the volume it measures. A
    /// compiler told nothing writes its intermediates next to the sources, and
    /// in a real checkout the first sign would be a diff nobody made.
    /// </remarks>
    [Fact]
    public async Task NoRule_WritesToTheWorkspace()
    {
        foreach (var (name, root, policy, changed) in EveryArrangement())
        {
            var before = Snapshot(root);

            foreach (var rule in BuiltInRuleDescriptorsTests.Discovered())
            {
                await Run(rule, root, policy, changed);
            }

            Snapshot(root).ShouldBe(before, name);
        }
    }

    /// <summary>
    /// The good fixture and every broken one, each with the arrangement it was
    /// built for.
    /// </summary>
    /// <remarks>
    /// Each case carries its own change set. Handing the good fixture's change
    /// set to a broken directory names files that are not there, and a rule
    /// asked for the size of a file that does not exist throws — which would
    /// read as the rule being at fault rather than the arrangement.
    /// </remarks>
    private static IEnumerable<(string Name, DirectoryInfo Root, IPolicyReader Policy, IReadOnlyList<ChangedFile> Changed)>
        EveryArrangement()
    {
        yield return ("workspace-good", Fixture("workspace-good"), Configured(), GoodChanges());

        foreach (var fixture in BrokenFixtures)
        {
            var broken = CaseFor(fixture);

            yield return (
                fixture,
                Fixture("workspace-broken", broken.Directory),
                broken.Policy,
                broken.ChangedFiles);
        }
    }

    /// <summary>
    /// No rule fails without saying how to fix it.
    /// </summary>
    /// <remarks>
    /// Across every rule and every broken fixture rather than per rule, because
    /// the way this regresses is a new finding added to an existing rule, not a
    /// new rule.
    /// </remarks>
    [Fact]
    public async Task NoRule_ReportsAProblemWithoutARemedy()
    {
        foreach (var fixture in BrokenFixtures)
        {
            var broken = CaseFor(fixture);
            var root = Fixture("workspace-broken", broken.Directory);

            foreach (var rule in BuiltInRuleDescriptorsTests.Discovered())
            {
                var outcome = await Run(rule, root, broken.Policy, broken.ChangedFiles);

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
