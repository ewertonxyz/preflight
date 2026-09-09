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
/// Runs the whole built-in set against real directories on real disk.
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
/// <see cref="ProcessRunner"/>, <see cref="PhysicalVolumeProbe"/> and
/// <see cref="ProcessEnvironmentProbe"/>, which live in <c>Preflight.Core</c>
/// precisely so this layer could exist without a test project referencing an
/// executable.
/// </para>
/// </remarks>
public sealed class BuiltInRulesIntegrationTests
{
    private static readonly PhysicalFileSystem FileSystem = new();
    private static readonly ProcessRunner Processes = new();
    private static readonly PhysicalVolumeProbe Volumes = new();
    private static readonly ProcessEnvironmentProbe Environment = new();

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
        "line-endings",
        "mutable-reference",
        "merge-artifact",
    ];

    /// <summary>
    /// The rules that cannot reach a pass against the good fixture, and why
    /// each one cannot.
    /// </summary>
    /// <remarks>
    /// Named one at a time with the reason beside the name, and never a blanket
    /// "not applicable is acceptable". The point of the test they exempt is
    /// that a rule reporting it checked nothing is not a rule that passed, and
    /// an exemption written as a general tolerance would hand that back to
    /// every rule at once, including the ones the test was written to catch.
    ///
    /// One entry: this repository has no submodule, and adding one to the
    /// fixture changes what every pipeline has to clone in order to run the
    /// suite at all.
    /// </remarks>
    private static readonly Dictionary<string, string> NotApplicableOnTheGoodFixture = new(StringComparer.Ordinal)
    {
        ["core.workspace.submodule-pin"] =
            "this repository has no submodule, and adding one changes what every checkout must clone",
    };

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
        "platform-sdk" => new BrokenCase(
            name,
            "core.build.platform-sdk",
            RuleStatus.Failed,
            Configured(sdkMinimum: "999.0.0"),
            []),

        "line-endings" => new BrokenCase(
            name,
            "core.presubmit.line-endings",
            RuleStatus.Failed,
            Configured(),
            [RuleFixture.Added("src/build.sh")]),

        "mutable-reference" => new BrokenCase(
            name,
            "core.presubmit.mutable-reference",
            RuleStatus.Failed,
            Configured(),
            [RuleFixture.Added("ci/pipeline.yml")]),

        "merge-artifact" => new BrokenCase(
            name,
            "core.presubmit.merge-artifact",
            RuleStatus.Failed,
            Configured(),
            [RuleFixture.Added("src/Program.cs")]),

        // Written out, with a default that throws rather than a catch-all. With
        // this many cases a mistyped name falling through would run some other
        // fixture's arrangement and report green about a directory nobody
        // checked.
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No broken fixture by that name."),
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
        RuleFixture.Added("ci/pipeline.yml"),
    ];

    private static readonly string[] PngMetaPair = ["**/*.png -> {dir}/{name}.{ext}.meta"];

    private static readonly string[] SerilogApproved = ["Serilog@3.1.1"];

    private static readonly string[] VersionArgument = ["--version"];

    private static readonly string[] SourceIsLf = ["**/*.cs", "**/*.sh"];

    private static readonly string[] ActionPin = [@"**/*.yml -> uses: \S+@(\S+)"];

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
            ["lf"] = SourceIsLf,
            ["pinned"] = ActionPin,
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

            // The shipped probes, not substitutes. Leaving either out compiles
            // perfectly and makes the rule that needs it report that it checked
            // nothing on every fixture, which is exactly the silence this layer
            // exists to break.
            Volumes = Volumes,
            Environment = Environment,
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
    /// Half the set checks nothing until it is configured, so a run that
    /// accepted n/a would be green having exercised none of them — which is what
    /// the companion test below, on its own, would have allowed.
    ///
    /// The exemptions are named one by one with the reason for each, rather
    /// than the test being relaxed to accept n/a in general. A general
    /// tolerance would hand the same escape to every rule.
    /// </remarks>
    [Fact]
    public async Task EveryRule_AgainstTheGoodWorkspace_ReachesItsPositivePath()
    {
        var root = Fixture("workspace-good");

        foreach (var rule in BuiltInRuleDescriptorsTests.Discovered())
        {
            var outcome = await Run(rule, root, Configured(), GoodChanges());

            if (NotApplicableOnTheGoodFixture.TryGetValue(rule.Descriptor.Id.Value, out var reason))
            {
                outcome.Status.ShouldBe(
                    RuleStatus.NotApplicable,
                    $"{rule.Descriptor.Id} is exempt because {reason}, so it must report exactly that.");

                continue;
            }

            outcome.Status.ShouldBe(
                RuleStatus.Passed,
                $"{rule.Descriptor.Id} on the good fixture: {Describe(outcome)}");
        }
    }

    /// <summary>
    /// Exactly which rules reach a verdict when nothing is in policy.
    /// </summary>
    /// <remarks>
    /// Two different things get a rule as far as a verdict, and the list makes
    /// the difference visible. Most of these are here because the fixture's
    /// <em>manifest</em> declares what they check — which tools, which
    /// dependencies, which settings, which variables — and a workspace that
    /// declared none of it would leave them silent. Only two are here without
    /// having been told anything at all: nobody names a file after a reserved
    /// device on purpose, and nobody commits a conflict marker on purpose, so
    /// requiring configuration first would be asking somebody to switch on a
    /// search for a thing that is never deliberate.
    ///
    /// Asserted as an exact set rather than as "at least these", because the
    /// property worth defending is that the list does not quietly grow. A rule
    /// that starts reporting on an unconfigured repository is a decision
    /// somebody has to argue, and this is the line that makes them.
    /// </remarks>
    [Fact]
    public async Task AgainstTheGoodWorkspace_WithNothingInPolicy_ExactlyTheseRulesReachAVerdict()
    {
        var root = Fixture("workspace-good");
        var reporting = new List<string>();

        foreach (var rule in BuiltInRuleDescriptorsTests.Discovered())
        {
            var outcome = await Run(rule, root, changed: GoodChanges());

            if (outcome.Status != RuleStatus.NotApplicable)
            {
                reporting.Add(rule.Descriptor.Id.Value);
            }
        }

        reporting.Order(StringComparer.Ordinal).ShouldBe([
            "core.build.compile-probe",
            "core.build.configuration",
            "core.presubmit.forbidden-paths",
            "core.presubmit.large-file",
            "core.presubmit.merge-artifact",
            "core.presubmit.path-portability",
            "core.workspace.dependencies",
            "core.workspace.environment",
            "core.workspace.free-space",
            "core.workspace.toolchain",
            "core.workspace.vcs-configuration",
        ]);
    }

    /// <summary>
    /// Every exemption names a rule that exists.
    /// </summary>
    /// <remarks>
    /// An exemption left behind after a rule was renamed is an exemption
    /// nothing uses and nobody notices, and the test above would then be
    /// excusing a rule that is no longer there while demanding a pass from the
    /// one it was written for.
    /// </remarks>
    [Fact]
    public void EveryExemption_NamesARuleInTheSet() =>
        NotApplicableOnTheGoodFixture.Keys.ShouldBeSubsetOf(
            BuiltInRuleDescriptorsTests.Discovered().Select(rule => rule.Descriptor.Id.Value));

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
