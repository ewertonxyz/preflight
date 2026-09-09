namespace Preflight.Rules.Tests.Workspace;

using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Rules;
using static Preflight.Rules.Tests.RuleFixture;

/// <summary>
/// Fixes <see cref="EnvironmentRule"/>, and above all the guarantee that no
/// value it read can ever leave it.
/// </summary>
public sealed class EnvironmentRuleTests
{
    private readonly EnvironmentRule _rule = new();

    private const string OneVariable = """
        { "tools": [], "environment": ["ANDROID_NDK_ROOT"] }
        """;

    /// <summary>
    /// A host that offers no probe reports that nothing was checked.
    /// </summary>
    /// <remarks>
    /// The most expensive false green available here, and it is reached by the
    /// fixture's own default: forget to wire the probe anywhere along the chain
    /// and this rule reports "not applicable" forever, on a workspace that
    /// declared exactly what it needed.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithNoProbeOffered_ReportsNotApplicable()
    {
        var outcome = await Run(OneVariable, environment: null);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
        outcome.Findings.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithNoManifest_ReportsNotApplicableWithoutAskingTheProbe()
    {
        var probe = Substitute.For<IEnvironmentProbe>();

        var outcome = await Run(null, probe);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
        probe.DidNotReceive().Read(Arg.Any<string>());
    }

    [Fact]
    public async Task ExecuteAsync_WithMalformedManifest_ReportsTheSharedSyntaxFinding()
    {
        var outcome = await Run("{ nope", Reporting("set"));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.Single().Message.ShouldBe("The workspace manifest is not valid JSON.");
    }

    [Fact]
    public async Task ExecuteAsync_WithNoEnvironmentKey_ReportsNotApplicable() =>
        (await Run("""{ "tools": [] }""", Reporting("set"))).Status.ShouldBe(RuleStatus.NotApplicable);

    /// <remarks>
    /// A distinct arm from the key being absent. A workspace that wrote the key
    /// and left it empty declared nothing.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAnEmptyList_ReportsNotApplicable() =>
        (await Run("""{ "tools": [], "environment": [] }""", Reporting("set"))).Status
            .ShouldBe(RuleStatus.NotApplicable);

    /// <summary>
    /// With neither a probe nor a manifest, the absent probe is the answer.
    /// </summary>
    /// <remarks>
    /// Which of the two wins would otherwise be whatever the code happened to
    /// check first. The probe is checked first on purpose: a host offering none
    /// cannot answer for any manifest, so reading the file to find out would be
    /// work whose result is already decided.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithNoProbeAndNoManifest_NeverOpensTheManifest()
    {
        var fileSystem = Substitute.For<IFileSystem>();

        var outcome = await _rule.ExecuteAsync(
            Context(fileSystem: fileSystem, stage: ValidationStage.Workspace),
            CancellationToken.None);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
        fileSystem.DidNotReceive().FileExists(Arg.Any<string>());
    }

    [Theory]
    [InlineData(null, RuleStatus.Failed)]
    [InlineData("", RuleStatus.Failed)]
    [InlineData("   ", RuleStatus.Failed)]
    [InlineData("/opt/ndk", RuleStatus.Passed)]
    public async Task ExecuteAsync_ForEachFormOfBeingUnset_ReportsTheExpectedStatus(string? value, RuleStatus status) =>
        (await Run(OneVariable, Reporting(value))).Status.ShouldBe(status);

    /// <remarks>
    /// Two ways of being unset, told apart, because the remedies differ: a
    /// variable nobody defined is a line missing from the runner's
    /// configuration, and one that arrived empty is a secret that exists and
    /// resolved to nothing, which is what a fork build does.
    /// </remarks>
    [Theory]
    [InlineData(null, "not set")]
    [InlineData("  ", "set, but empty")]
    public async Task ExecuteAsync_ForEachFormOfBeingUnset_SaysWhichOneItWas(string? value, string actual)
    {
        var finding = (await Run(OneVariable, Reporting(value))).Findings.Single();

        finding.Expected.ShouldBe("set to a non-empty value");
        finding.Actual.ShouldBe(actual);
    }

    /// <summary>
    /// A value that is set never appears anywhere, in any field or any log
    /// line.
    /// </summary>
    /// <remarks>
    /// The assertion the whole rule is shaped around. There is no code path
    /// here that holds a value except to ask whether it is empty, and this is
    /// what keeps it that way — including the logger, because a debug line
    /// reaches a build log exactly as a finding does.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAValueSet_NeverPutsItInAFindingOrALogLine()
    {
        const string Secret = "s3cr3t-marker";

        var logger = Substitute.For<IRuleLogger>();
        var probe = Substitute.For<IEnvironmentProbe>();

        probe.Read("SIGNING_KEY").Returns(Secret);
        probe.Read("MISSING").Returns((string?)null);

        var outcome = await _rule.ExecuteAsync(
            new RuleContext
            {
                WorkspaceRoot = WorkspaceRoot,
                Stage = ValidationStage.Workspace,
                Target = new BuildTarget("win64", "Development"),
                ChangedFiles = [],
                Policy = EmptyPolicy(),
                Logger = logger,
                FileSystem = Workspace("""{ "tools": [], "environment": ["SIGNING_KEY", "MISSING"] }"""),
                Processes = Substitute.For<IProcessRunner>(),
                Environment = probe,
            },
            CancellationToken.None);

        outcome.Status.ShouldBe(RuleStatus.Failed);

        outcome.Findings
            .SelectMany(finding => new[]
            {
                finding.Message,
                finding.Expected,
                finding.Actual,
                finding.Remediation,
                finding.Location?.RelativePath,
            })
            .OfType<string>()
            .ShouldAllBe(field => !field.Contains(Secret, StringComparison.Ordinal));

        logger.ReceivedCalls().ShouldBeEmpty();
    }

    /// <remarks>
    /// One finding per declared entry, duplicates included. The manifest is
    /// what the reader is looking at, so a name written twice is answered twice
    /// rather than collapsed into one line matching neither of the two they can
    /// see.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithADuplicatedName_ReportsOnePerDeclaredEntry()
    {
        var outcome = await Run("""{ "tools": [], "environment": ["A", "A"] }""", Reporting(null));

        outcome.Findings.Count.ShouldBe(2);
    }

    /// <remarks>
    /// Never skipped. A verification switched off by a typo, reported green, is
    /// the exact failure this rule exists to prevent one layer up.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAnEmptyName_ReportsTheManifestKeyAndTheIndex()
    {
        var probe = Reporting("set");

        var outcome = await Run("""{ "tools": [], "environment": ["", "B"] }""", probe);

        outcome.Status.ShouldBe(RuleStatus.Failed);

        var finding = outcome.Findings.Single();

        finding.Message.ShouldContain("Entry 0");
        finding.Remediation!.ShouldContain("'environment'");

        probe.DidNotReceive().Read(string.Empty);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_DoesNotSwallowTheCancellation()
    {
        using var source = new CancellationTokenSource();

        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _rule.ExecuteAsync(
                Context(
                    fileSystem: Workspace(OneVariable),
                    environment: Reporting(null),
                    stage: ValidationStage.Workspace),
                source.Token));
    }

    private Task<RuleOutcome> Run(string? manifest, IEnvironmentProbe? environment) =>
        _rule.ExecuteAsync(
            Context(
                fileSystem: Workspace(manifest),
                environment: environment,
                stage: ValidationStage.Workspace),
            CancellationToken.None);

    private static IFileSystem Workspace(string? manifest)
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.FileExists(Arg.Any<string>()).Returns(manifest is not null);

        if (manifest is not null)
        {
            fileSystem.ReadAllTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(manifest);
        }

        return fileSystem;
    }

    private static IEnvironmentProbe Reporting(string? value)
    {
        var probe = Substitute.For<IEnvironmentProbe>();

        probe.Read(Arg.Any<string>()).Returns(value);

        return probe;
    }
}
