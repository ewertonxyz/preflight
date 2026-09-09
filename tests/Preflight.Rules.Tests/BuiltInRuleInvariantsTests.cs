namespace Preflight.Rules.Tests;

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Rules;
using static Preflight.Rules.Tests.RuleFixture;

/// <summary>
/// What has to be true of every built-in rule, whatever it checks.
/// </summary>
/// <remarks>
/// <para>
/// One statement per invariant rather than one copy per rule. These are
/// properties of the set, and eighteen copies of each is exactly what a layer
/// like this exists to avoid — half of them would drift, and the drift would
/// read as a deliberate exception.
/// </para>
/// <para>
/// The failure modes are hand-written rather than driven by the fixtures on
/// disk. A loop over fixtures reaches only the failures a fixture happens to
/// produce, and half of these cannot be produced by a directory at all: a
/// reserved device name and a colon in a file name cannot exist in a Windows
/// working tree.
/// </para>
/// </remarks>
public sealed class BuiltInRuleInvariantsTests
{
    private static readonly string[] VersionArgument = ["--version"];

    private static readonly string[] SerilogOnly = ["Serilog@1.0.0"];

    private static readonly string[] UassetPair = ["**/*.uasset -> {dir}/{name}.uexp"];

    /// <summary>
    /// The whole built-in set, as the tool discovers them.
    /// </summary>
    private static IReadOnlyList<IValidationRule> Rules() => BuiltInRuleDescriptorsTests.Discovered();

    /// <summary>
    /// One arranged failure per rule, by name.
    /// </summary>
    /// <remarks>
    /// Strings rather than the arrangements themselves, so that xUnit can
    /// serialise them and a failing row names the rule it was about.
    /// </remarks>
    private static readonly string[] Failures =
    [
        "toolchain-missing-manifest",
        "toolchain-tool-absent",
        "dependencies-no-version",
        "forbidden-path",
        "large-file",
        "build-config-missing",
        "compile-probe-fails",
        "lfs-real-blob",
        "path-portability-reserved-name",
        "companion-missing",
        "free-space-below-floor",
        "approved-dependency-not-listed",
        "platform-sdk-absent",
        "vcs-setting-not-set",
        "environment-not-set",
        "submodule-diverged",
        "line-endings-crlf-where-lf-required",
        "mutable-reference-moves",
        "merge-artifact-marker",
    ];

    /// <summary>
    /// Arrangements that leave a rule with nothing to check.
    /// </summary>
    private static readonly string[] NotApplicable =
    [
        "toolchain-no-tools",
        "dependencies-none",
        "forbidden-no-changes",
        "large-file-no-changes",
        "lfs-no-attributes",
        "path-portability-no-changes",
        "companion-no-pairs",
        "free-space-no-entries",
        "approved-no-list",
        "platform-sdk-unconfigured",
        "vcs-no-requirement",
        "environment-no-probe",
        "submodule-none",
        "line-endings-nothing-declared",
        "mutable-reference-no-entries",
        "merge-artifact-no-changes",
    ];

    /// <summary>
    /// Arrangements in which a rule looked and found nothing wrong.
    /// </summary>
    /// <remarks>
    /// The third quadrant, and the one the file used to have no room for. An
    /// assertion that a rule did not fail is satisfied by a rule that reported
    /// it checked nothing, so every "never reported" decision — a line of
    /// equals signs, a marker in the middle of a line — was being asserted by a
    /// test that could not tell the two apart.
    /// </remarks>
    private static readonly string[] Passes =
    [
        "vcs-setting-matches",
        "environment-set",
        "submodule-up-to-date",
        "line-endings-lf-where-lf-required",
        "mutable-reference-pinned",
        "merge-artifact-heading-underline",
        "merge-artifact-marker-mid-line",
    ];

    /// <remarks>
    /// The lists are arrays that the theory sources copy, rather than the
    /// theory sources themselves, so that the guard below can read the same
    /// names without reaching into how the test framework carries a row.
    /// </remarks>
    public static TheoryData<string> FailureModes() => [.. Failures];

    public static TheoryData<string> NotApplicableModes() => [.. NotApplicable];

    public static TheoryData<string> PassedModes() => [.. Passes];

    [Fact]
    public void BuiltInRuleIds_HoldsExactlyTheIdsTheAssemblyDeclares()
    {
        var declared = typeof(BuiltInRuleIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(RuleId))
            .Select(field => ((RuleId)field.GetValue(null)!).Value)
            .Order(StringComparer.Ordinal);

        var discovered = Rules().Select(rule => rule.Descriptor.Id.Value).Order(StringComparer.Ordinal);

        // Both directions. A constant left behind after a rule was deleted is
        // as wrong as a rule whose id nobody declared, and only one of the two
        // is caught by comparing in one direction.
        declared.ShouldBe(discovered);
    }

    /// <remarks>
    /// A rule that reads state living outside the workspace cannot describe its
    /// own inputs, and a cacheable one that tried would serve a pass computed
    /// against a disk that has since filled up.
    /// </remarks>
    [Fact]
    public void ExactlyOneBuiltInRule_IsCacheable()
    {
        Rules()
            .Where(rule => rule is ICacheableRule)
            .Select(rule => rule.Descriptor.Id.Value)
            .ShouldBe([BuiltInRuleIds.CompileProbe.Value]);
    }

    /// <summary>
    /// Every path check written is a path check that runs.
    /// </summary>
    /// <remarks>
    /// The perfect false green: a check that compiles, has green tests of its
    /// own, and is never reached because nobody added it to the composed list.
    /// Read by reflection because the interface is internal, and by its declared
    /// name rather than by instantiating each type — one of them takes
    /// collaborators, and a test that had to build them would be asserting the
    /// arrange.
    /// </remarks>
    [Fact]
    public void EveryPathPortabilityCheckInTheAssembly_IsComposedByTheRule()
    {
        var assembly = typeof(BuiltInRuleIds).Assembly;

        var contract = assembly.GetTypes()
            .Single(type => type.IsInterface && type.Name == "IPathPortabilityCheck");

        var names = assembly.GetTypes()
            .Where(type => contract.IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
            .Select(type => type.GetField("CheckName", BindingFlags.Public | BindingFlags.Static)
                ?.GetRawConstantValue() as string)
            .ToArray();

        names.ShouldAllBe(name => name != null, "Every check declares the name the policy selects it by.");

        names.ShouldBeSubsetOf(
            PathPortabilityRule.DefaultChecks,
            "A check the rule never composes compiles, tests green, and never runs.");

        names.Length.ShouldBe(PathPortabilityRule.DefaultChecks.Length);
    }

    /// <summary>
    /// Nothing in the assembly holds static state that can be written.
    /// </summary>
    /// <remarks>
    /// Rules at the same level of the graph run concurrently, so a static field
    /// that can be written is a race the suite would only find intermittently.
    ///
    /// Every type, not only the rules, and the difference is not theoretical: a
    /// dictionary memoising compiled expressions in a collaborator is the first
    /// optimisation anybody reaches for on seeing the same expression built once
    /// per file, and a guard that looked only at rule classes would not see it.
    ///
    /// Compiler-generated types are left out because their static fields are the
    /// caches the compiler emits for lambdas that capture nothing. Those are
    /// written once and never mutated afterwards, and nothing anybody writes
    /// here can make one of them a race.
    /// </remarks>
    [Fact]
    public void NothingInTheRulesAssembly_HoldsMutableStaticState()
    {
        var mutable = typeof(BuiltInRuleIds).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .SelectMany(type => type
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(field => !field.IsInitOnly && !field.IsLiteral)
                .Select(field => $"{type.Name}.{field.Name}"))
            .ToArray();

        mutable.ShouldBeEmpty();
    }

    /// <summary>
    /// Composition over inheritance, made verifiable.
    /// </summary>
    /// <remarks>
    /// A base class between rules would own <c>ExecuteAsync</c> and hand each
    /// rule a template — a larger promise than any two of them share, and one an
    /// external plugin author has no access to at all.
    /// </remarks>
    [Fact]
    public void NoBuiltInRule_InheritsFromAnythingButObject()
    {
        foreach (var rule in Rules())
        {
            rule.GetType().BaseType.ShouldBe(typeof(object), rule.GetType().Name);
            rule.GetType().IsSealed.ShouldBeTrue(rule.GetType().Name);
        }
    }

    /// <summary>
    /// No rule reports a problem without saying how to fix it.
    /// </summary>
    /// <remarks>
    /// The half of the admission criterion a machine can defend. A rule that
    /// says what is wrong and not what to do about it delivers half the work,
    /// to somebody who is mid-task and did not choose to be reading it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(FailureModes))]
    public async Task EveryFailureMode_CarriesARemediation(string mode)
    {
        var outcome = await RunAsync(mode);

        outcome.Findings.ShouldNotBeEmpty(mode);
        outcome.Findings.ShouldAllBe(finding => !string.IsNullOrWhiteSpace(finding.Remediation), mode);
    }

    /// <summary>
    /// No finding carries an absolute path.
    /// </summary>
    /// <remarks>
    /// An absolute path carries the name of the account that ran the tool, and
    /// every one of these strings reaches a build log that far more people read
    /// than ran the build. It is also non-deterministic: the same commit
    /// validated on two machines would produce two different reports.
    /// </remarks>
    [Theory]
    [MemberData(nameof(FailureModes))]
    public async Task NoFinding_NamesAnAbsolutePathOrTheWorkspaceRoot(string mode)
    {
        var outcome = await RunAsync(mode);

        foreach (var field in outcome.Findings.SelectMany(Fields).OfType<string>())
        {
            field.ShouldNotContain(WorkspaceRoot.FullName, Case.Insensitive, mode);

            // A drive letter followed by an actual path segment. A bare volume
            // root is not a leak — it is how the operating system names a
            // volume, and the free-space finding says which one it measured.
            // What must never appear is a path under it, because that carries
            // the account directory of whoever ran the tool.
            field.ShouldNotMatch(@"[A-Za-z]:[\\/][\w.]", mode);
        }
    }

    [Theory]
    [MemberData(nameof(NotApplicableModes))]
    public async Task EveryNotApplicableOutcome_CarriesNoFindings(string mode)
    {
        var outcome = await RunAsync(mode);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable, mode);
        outcome.Findings.ShouldBeEmpty(mode);
    }

    /// <summary>
    /// A rule that says it passed actually looked.
    /// </summary>
    /// <remarks>
    /// The assertion the other two cannot make between them. "Did not fail" is
    /// satisfied by a rule that reported it checked nothing, so a decision never
    /// to report something — a heading underline, a marker in the middle of a
    /// line — is only really pinned by a run that examined the file and passed
    /// it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PassedModes))]
    public async Task EveryPassedOutcome_CarriesNoFindingsAndIsNotNotApplicable(string mode)
    {
        var outcome = await RunAsync(mode);

        outcome.Status.ShouldBe(RuleStatus.Passed, mode);
        outcome.Findings.ShouldBeEmpty(mode);
    }

    /// <summary>
    /// Every rule has a failure mode listed, and every listed mode is a rule.
    /// </summary>
    /// <remarks>
    /// The highest-leverage line in the file. Without it a rule with no row is
    /// exempt from every invariant above — remediation, absolute paths, refusing
    /// to claim the tool's own statuses — and nothing anywhere says so. Both
    /// directions, because a mode left behind after a rule was deleted runs an
    /// arrangement against nothing.
    /// </remarks>
    [Fact]
    public void EveryRule_HasAFailureModeAndEveryFailureModeHasARule()
    {
        // Distinct, because a rule may have more than one arranged failure and
        // the toolchain rule has two. What is asserted is the set of rules the
        // list reaches, not how many ways it reaches each of them.
        Failures.Select(RuleOf).Distinct().Order(StringComparer.Ordinal).ShouldBe(
            Rules().Select(rule => rule.Descriptor.Id.Value).Order(StringComparer.Ordinal),
            "A rule with no failure mode is a rule no invariant here applies to.");
    }

    [Theory]
    [MemberData(nameof(FailureModes))]
    public async Task EveryFailureMode_CarriesAtLeastOneFinding(string mode)
    {
        (await RunAsync(mode)).Findings.ShouldNotBeEmpty(mode);
    }

    /// <remarks>
    /// Both statuses belong to the tool: <c>Skipped</c> is what a dependency's
    /// failure produces, and <c>Errored</c> is what an escaping exception
    /// produces. A rule returning either would be claiming the tool's voice.
    /// </remarks>
    [Theory]
    [MemberData(nameof(FailureModes))]
    public async Task NoRule_ReturnsSkippedOrErroredItself(string mode)
    {
        var outcome = await RunAsync(mode);

        outcome.Status.ShouldNotBe(RuleStatus.Skipped, mode);
        outcome.Status.ShouldNotBe(RuleStatus.Errored, mode);
    }


    private static IEnumerable<string?> Fields(Finding finding) =>
        [finding.Message, finding.Expected, finding.Actual, finding.Remediation, finding.Location?.RelativePath];

    /// <summary>
    /// One rule and the context that drives it to the named outcome.
    /// </summary>
    /// <remarks>
    /// The rule is carried beside the context rather than executed on the spot,
    /// so that the guard pairing the mode lists against the discovered set can
    /// ask which rule a mode is about without running it. A second table mapping
    /// modes to rules would answer the same question and would be free to
    /// disagree with this one.
    /// </remarks>
    private sealed record Arrangement(IValidationRule Rule, RuleContext Context);

    /// <summary>
    /// Runs the rule the mode names, arranged to reach that mode.
    /// </summary>
    private static Task<RuleOutcome> RunAsync(string mode)
    {
        var arrangement = Arranged(mode);

        return arrangement.Rule.ExecuteAsync(arrangement.Context, CancellationToken.None);
    }

    private static string RuleOf(string mode) => Arranged(mode).Rule.Descriptor.Id.Value;

    /// <remarks>
    /// Every arm is written out and the default throws. With this many rows a
    /// mistyped mode falling through to a catch-all would quietly run some other
    /// rule's arrangement and report green about a rule nobody exercised — which
    /// is the failure every invariant in this file exists to prevent, arriving
    /// through the harness instead of through the product.
    /// </remarks>
    private static Arrangement Arranged(string mode) => mode switch
    {
        "toolchain-missing-manifest" => Run(new ToolchainRule(), Manifest(null)),
        "toolchain-tool-absent" => Run(
            new ToolchainRule(),
            Manifest(ToolManifest),
            processes: Failing()),
        "toolchain-no-tools" => Run(new ToolchainRule(), Manifest("""{ "tools": [] }""")),

        "dependencies-no-version" => Run(
            new DependenciesRule(),
            Manifest("""{ "tools": [], "dependencies": [{ "id": "Serilog" }] }""")),
        "dependencies-none" => Run(new DependenciesRule(), Manifest("""{ "tools": [] }""")),

        "forbidden-path" => Run(new ForbiddenPathsRule(), changed: [Added("secrets/key.pfx")]),
        "forbidden-no-changes" => Run(new ForbiddenPathsRule()),

        "large-file" => Run(new LargeFileRule(), Sized(50 * 1024 * 1024), changed: [Added("art/big.bin")]),
        "large-file-no-changes" => Run(new LargeFileRule()),

        "build-config-missing" => Run(new BuildConfigurationRule(), Manifest(null)),
        "compile-probe-fails" => Run(
            new CompileProbeRule(),
            Manifest(ProbeManifest),
            processes: Exiting(1, "the build failed")),

        "lfs-real-blob" => Run(
            new LfsPointerRule(),
            Attributes("*.psd filter=lfs", "art/a.psd", [0x89, 0x50, 0x4E, 0x47]),
            changed: [Added("art/a.psd")],
            policy: PolicyWith("attributesPath", "lfs.attributes")),
        "lfs-no-attributes" => Run(new LfsPointerRule(), changed: [Added("art/a.psd")]),

        "path-portability-reserved-name" => Run(new PathPortabilityRule(), changed: [Added("src/CON.cs")]),
        "path-portability-no-changes" => Run(new PathPortabilityRule()),

        "companion-missing" => Run(
            new CompanionFileRule(),
            changed: [Added("art/a.uasset")],
            policy: PolicyWith("pairs", UassetPair)),
        "companion-no-pairs" => Run(new CompanionFileRule(), changed: [Added("art/a.uasset")]),

        "free-space-below-floor" => Run(
            new FreeSpaceRule(),
            Manifest(FreeSpaceManifest),
            volumes: ProbeReporting(1)),
        "free-space-no-entries" => Run(new FreeSpaceRule(), Manifest("""{ "tools": [] }""")),

        "approved-dependency-not-listed" => Run(
            new ApprovedDependenciesRule(),
            Manifest("""{ "tools": [], "dependencies": [{ "id": "Newtonsoft.Json", "version": "13.0.3" }] }"""),
            policy: PolicyWith("approved", SerilogOnly)),
        "approved-no-list" => Run(
            new ApprovedDependenciesRule(),
            Manifest("""{ "tools": [], "dependencies": [{ "id": "Serilog", "version": "1.0.0" }] }""")),

        "platform-sdk-absent" => Run(
            new PlatformSdkRule(),
            processes: Failing(),
            policy: PolicyWith(new Dictionary<string, object?>
            {
                ["sdk.command"] = "winsdk",
                ["sdk.name"] = "Windows SDK",
                ["sdk.arguments"] = VersionArgument,
            })),
        "platform-sdk-unconfigured" => Run(new PlatformSdkRule()),

        "vcs-setting-not-set" => Run(
            new VcsConfigurationRule(),
            Manifest(VcsManifest),
            processes: Repository(setting: Exit(1))),
        "vcs-setting-matches" => Run(
            new VcsConfigurationRule(),
            Manifest(VcsManifest),
            processes: Repository(setting: Printing("true"))),
        "vcs-no-requirement" => Run(new VcsConfigurationRule(), Manifest("""{ "tools": [] }""")),

        "environment-not-set" => Run(
            new EnvironmentRule(),
            Manifest(EnvironmentManifest),
            environment: Reporting(null)),
        "environment-set" => Run(
            new EnvironmentRule(),
            Manifest(EnvironmentManifest),
            environment: Reporting("a value")),
        "environment-no-probe" => Run(new EnvironmentRule(), Manifest(EnvironmentManifest)),

        "submodule-diverged" => Run(
            new SubmodulePinRule(),
            Manifest(null),
            processes: Repository(submodules: Printing("+0123456789abcdef ext/lib (v1)\n"))),
        "submodule-up-to-date" => Run(
            new SubmodulePinRule(),
            Manifest(null),
            processes: Repository(submodules: Printing(" 0123456789abcdef ext/lib (v1)\n"))),
        "submodule-none" => Run(
            new SubmodulePinRule(),
            Manifest(null),
            processes: Repository(submodules: Printing(string.Empty))),

        "line-endings-crlf-where-lf-required" => Run(
            new LineEndingsRule(),
            Text("src/build.sh", "set -e\r\nmake\r\n"),
            changed: [Added("src/build.sh")],
            policy: PolicyWith("lf", ShellScripts)),
        "line-endings-lf-where-lf-required" => Run(
            new LineEndingsRule(),
            Text("src/build.sh", "set -e\nmake\n"),
            changed: [Added("src/build.sh")],
            policy: PolicyWith("lf", ShellScripts)),
        "line-endings-nothing-declared" => Run(
            new LineEndingsRule(),
            Text("src/build.sh", "set -e\r\n"),
            changed: [Added("src/build.sh")]),

        "mutable-reference-moves" => Run(
            new MutableReferenceRule(),
            Text("ci/build.yml", "  uses: some/action@main\n"),
            changed: [Added("ci/build.yml")],
            policy: PolicyWith("pinned", ActionPin)),
        "mutable-reference-pinned" => Run(
            new MutableReferenceRule(),
            Text("ci/build.yml", "  uses: some/action@" + Sha + "\n"),
            changed: [Added("ci/build.yml")],
            policy: PolicyWith("pinned", ActionPin)),
        "mutable-reference-no-entries" => Run(
            new MutableReferenceRule(),
            Text("ci/build.yml", "  uses: some/action@main\n"),
            changed: [Added("ci/build.yml")]),

        "merge-artifact-marker" => Run(
            new MergeArtifactRule(),
            Text("src/Program.cs", "a\n<<<<<<< HEAD\nb\n"),
            changed: [Added("src/Program.cs")]),
        "merge-artifact-heading-underline" => Run(
            new MergeArtifactRule(),
            Text("README.md", "Title\n=======\n"),
            changed: [Added("README.md")]),
        "merge-artifact-marker-mid-line" => Run(
            new MergeArtifactRule(),
            Text("notes/release.md", "the marker <<<<<<< appears mid-line\n"),
            changed: [Added("notes/release.md")]),
        "merge-artifact-no-changes" => Run(new MergeArtifactRule()),

        _ => throw new ArgumentOutOfRangeException(
            nameof(mode),
            mode,
            "No arrangement by that name."),
    };

    private const string ToolManifest = """
        { "tools": [{ "name": ".NET SDK", "command": "dotnet", "arguments": ["--version"] }] }
        """;

    private const string ProbeManifest = """
        { "tools": [], "compileProbe": { "command": "dotnet", "arguments": ["build"] } }
        """;

    private const string FreeSpaceManifest = """
        { "tools": [], "freeSpace": [{ "path": ".", "minimumBytes": 999999999 }] }
        """;

    private const string VcsManifest = """
        { "tools": [], "vcs": { "settings": [{ "name": "core.longpaths", "expected": "true" }] } }
        """;

    private const string EnvironmentManifest = """
        { "tools": [], "environment": ["ANDROID_NDK_ROOT"] }
        """;

    /// <summary>
    /// A forty-character object name, which the default shape accepts.
    /// </summary>
    private static readonly string Sha = new('a', 40);

    private static readonly string[] ShellScripts = ["**/*.sh"];

    /// <summary>
    /// A pipeline step, and the reference at the end of it.
    /// </summary>
    /// <remarks>
    /// A literal space rather than a whitespace class, which is a smaller
    /// expression and also keeps the entry out of the way of the invariant that
    /// refuses an absolute path in a finding: this entry is quoted back in the
    /// remediation, and a letter followed by a colon and a backslash reads as a
    /// drive to a check that cannot know it is looking at an expression.
    /// </remarks>
    private static readonly string[] ActionPin = [@"**/*.yml -> uses: \S+@(\S+)"];

    private static Arrangement Run(
        IValidationRule rule,
        IFileSystem? fileSystem = null,
        IReadOnlyList<ChangedFile>? changed = null,
        IPolicyReader? policy = null,
        IProcessRunner? processes = null,
        IVolumeProbe? volumes = null,
        IEnvironmentProbe? environment = null) =>
        new(
            rule,
            Context(
                changedFiles: changed,
                policy: policy,
                fileSystem: fileSystem,
                processes: processes,
                volumes: volumes,
                environment: environment,
                stage: rule.Descriptor.Stage));

    private static IFileSystem Manifest(string? json)
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.FileExists(Arg.Any<string>()).Returns(json is not null);

        if (json is not null)
        {
            fileSystem.ReadAllTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(json);
        }

        return fileSystem;
    }

    private static IFileSystem Sized(long bytes)
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.GetFileSize(Arg.Any<string>()).Returns(bytes);

        return fileSystem;
    }

    /// <summary>
    /// A workspace holding one text file, matched by the tail of its path.
    /// </summary>
    /// <remarks>
    /// Matched by tail because the rule joins the path with a workspace root
    /// this fixture owns, and a comparison that rebuilt the join would be
    /// asserting the arrangement rather than the rule.
    /// </remarks>
    private static IFileSystem Text(string relativePath, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.FileExists(Arg.Any<string>()).Returns(call => Tail(call.Arg<string>(), relativePath));
        fileSystem.GetFileSize(Arg.Any<string>()).Returns(bytes.Length);

        fileSystem.OpenRead(Arg.Is<string>(path => Tail(path, relativePath)))
            .Returns(_ => new MemoryStream(bytes));

        return fileSystem;
    }

    private static bool Tail(string path, string relativePath) =>
        path.Replace('\\', '/').EndsWith(relativePath, StringComparison.Ordinal);

    private static IFileSystem Attributes(string attributes, string blobPath, byte[] blob)
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.FileExists(Arg.Any<string>()).Returns(true);
        fileSystem.ReadAllTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(attributes);

        // Compared with separators normalised. The rule combines the workspace
        // root with a relative path that uses forward slashes, so the string it
        // hands over carries both kinds and a literal comparison misses.
        fileSystem.OpenRead(Arg.Is<string>(path => Tail(path, blobPath))).Returns(_ => new MemoryStream(blob));

        return fileSystem;
    }

    private static IProcessRunner Failing()
    {
        var processes = Substitute.For<IProcessRunner>();

        processes.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProcessResult>>(_ => throw new InvalidOperationException("no such executable"));

        return processes;
    }

    private static IProcessRunner Exiting(int exitCode, string standardError)
    {
        var processes = Substitute.For<IProcessRunner>();

        processes.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(exitCode, string.Empty, standardError, TimeSpan.FromSeconds(1)));

        return processes;
    }

    /// <summary>
    /// A client that recognises the workspace and answers one further question.
    /// </summary>
    /// <remarks>
    /// Dispatched on the arguments rather than on the order of calls, because
    /// both rules that reach a client ask it two different things and a blind
    /// substitute would answer the first with what was meant for the second.
    /// </remarks>
    private static IProcessRunner Repository(ProcessResult? setting = null, ProcessResult? submodules = null)
    {
        var processes = Substitute.For<IProcessRunner>();

        processes.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ProcessRequest>().Arguments[0] switch
            {
                "rev-parse" => Printing(".git"),
                "config" => setting!,
                _ => submodules!,
            });

        return processes;
    }

    private static ProcessResult Printing(string standardOutput) =>
        new(0, standardOutput, string.Empty, TimeSpan.FromMilliseconds(5));

    private static ProcessResult Exit(int exitCode) =>
        new(exitCode, string.Empty, string.Empty, TimeSpan.FromMilliseconds(5));

    private static IEnvironmentProbe Reporting(string? value)
    {
        var probe = Substitute.For<IEnvironmentProbe>();

        probe.Read(Arg.Any<string>()).Returns(value);

        return probe;
    }

    private static IVolumeProbe ProbeReporting(long available)
    {
        var probe = Substitute.For<IVolumeProbe>();

        probe.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new VolumeSpace("D:\\", 100_000_000_000, available));

        return probe;
    }
}
