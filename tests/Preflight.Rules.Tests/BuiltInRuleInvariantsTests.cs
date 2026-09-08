namespace Preflight.Rules.Tests;

using System.Reflection;
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
/// properties of the set, and twelve copies of each is exactly what a layer
/// like this exists to avoid — six of them would drift, and the drift would
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
    /// The twelve rules, as the tool discovers them.
    /// </summary>
    private static IReadOnlyList<IValidationRule> Rules() => BuiltInRuleDescriptorsTests.Discovered();

    /// <summary>
    /// One arranged failure per rule, by name.
    /// </summary>
    /// <remarks>
    /// Strings rather than the arrangements themselves, so that xUnit can
    /// serialise them and a failing row names the rule it was about.
    /// </remarks>
    public static TheoryData<string> FailureModes() =>
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
    ];

    /// <summary>
    /// Arrangements that leave a rule with nothing to check.
    /// </summary>
    public static TheoryData<string> NotApplicableModes() =>
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
    ];

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

    /// <remarks>
    /// Rules at the same level of the graph run concurrently, so a static field
    /// that can be written is a race the suite would only find intermittently.
    /// </remarks>
    [Fact]
    public void NoBuiltInRule_HoldsMutableStaticState()
    {
        foreach (var rule in Rules())
        {
            var mutable = rule.GetType()
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(field => !field.IsInitOnly && !field.IsLiteral)
                .Select(field => $"{rule.GetType().Name}.{field.Name}");

            mutable.ShouldBeEmpty();
        }
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
    /// Runs the rule the mode names, arranged to reach that mode.
    /// </summary>
    private static Task<RuleOutcome> RunAsync(string mode) => mode switch
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
        _ => Run(new PlatformSdkRule()),
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

    private static Task<RuleOutcome> Run(
        IValidationRule rule,
        IFileSystem? fileSystem = null,
        IReadOnlyList<ChangedFile>? changed = null,
        IPolicyReader? policy = null,
        IProcessRunner? processes = null,
        IVolumeProbe? volumes = null) =>
        rule.ExecuteAsync(
            Context(
                changedFiles: changed,
                policy: policy,
                fileSystem: fileSystem,
                processes: processes,
                volumes: volumes,
                stage: rule.Descriptor.Stage),
            CancellationToken.None);

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

    private static IFileSystem Attributes(string attributes, string blobPath, byte[] blob)
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.FileExists(Arg.Any<string>()).Returns(true);
        fileSystem.ReadAllTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(attributes);
        // Compared with separators normalised. The rule combines the workspace
        // root with a relative path that uses forward slashes, so the string it
        // hands over carries both kinds and a literal comparison misses.
        fileSystem
            .OpenRead(Arg.Is<string>(path =>
                path.Replace('\\', '/').EndsWith(blobPath, StringComparison.Ordinal)))
            .Returns(_ => new MemoryStream(blob));

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

    private static IVolumeProbe ProbeReporting(long available)
    {
        var probe = Substitute.For<IVolumeProbe>();

        probe.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new VolumeSpace("D:\\", 100_000_000_000, available));

        return probe;
    }
}
