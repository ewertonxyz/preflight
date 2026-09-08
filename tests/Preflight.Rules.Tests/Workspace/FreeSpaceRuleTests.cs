namespace Preflight.Rules.Tests.Workspace;

using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Rules;
using static Preflight.Rules.Tests.RuleFixture;

/// <summary>
/// Fixes <see cref="FreeSpaceRule"/>.
/// </summary>
/// <remarks>
/// Every volume here is substituted, and none of it reaches a disk. A fixture
/// declaring a floor that a real disk fails is a fixture whose verdict depends
/// on the size of the machine running it — it fails everywhere today and passes
/// in silence on the first machine with a bigger array.
/// </remarks>
public sealed class FreeSpaceRuleTests
{
    private readonly FreeSpaceRule _rule = new();

    private static string Manifest(params string[] entries) => $$"""
        {
          "tools": [],
          "freeSpace": [{{string.Join(", ", entries)}}]
        }
        """;

    private static string Entry(string path, string minimum = "1000") =>
        $$"""{ "path": "{{path}}", "minimumBytes": {{minimum}} }""";

    private static IFileSystem ManifestContaining(string? json)
    {
        var fileSystem = Substitute.For<IFileSystem>();

        fileSystem.FileExists(Arg.Any<string>()).Returns(json is not null);

        if (json is not null)
        {
            fileSystem.ReadAllTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(json);
        }

        return fileSystem;
    }

    /// <summary>
    /// A probe answering with a fixed amount of room, or with nothing.
    /// </summary>
    private static IVolumeProbe ProbeReporting(params long?[] available)
    {
        var probe = Substitute.For<IVolumeProbe>();
        var answers = new Queue<long?>(available);

        probe.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var next = answers.Count > 0 ? answers.Dequeue() : available[^1];

            return Task.FromResult(next is { } bytes ? new VolumeSpace("D:\\", 100_000, bytes) : null);
        });

        return probe;
    }

    private Task<RuleOutcome> Run(string? manifest, IVolumeProbe? probe) =>
        _rule.ExecuteAsync(
            Context(
                fileSystem: ManifestContaining(manifest),
                volumes: probe,
                stage: ValidationStage.Workspace),
            CancellationToken.None);

    /// <remarks>
    /// The rule this one depends on already fails loudly on a missing manifest.
    /// Reporting it twice would put one problem on two lines and make the count
    /// in the summary disagree with the number of things to fix.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithNoManifest_IsNotApplicable()
    {
        (await Run(null, ProbeReporting(9_000))).Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <remarks>
    /// One file, one message. The dependency rule says exactly this about the
    /// same syntax error, and two rules describing one file two ways leaves the
    /// reader deciding which to believe.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithAMalformedManifest_Fails()
    {
        var space = await Run("{ nope", ProbeReporting(9_000));

        var dependencies = await new DependenciesRule().ExecuteAsync(
            Context(fileSystem: ManifestContaining("{ nope"), stage: ValidationStage.Workspace),
            CancellationToken.None);

        space.Status.ShouldBe(RuleStatus.Failed);

        var mine = space.Findings.ShouldHaveSingleItem();
        var theirs = dependencies.Findings.ShouldHaveSingleItem();

        mine.Message.ShouldBe(theirs.Message);
        mine.Actual.ShouldBe(theirs.Actual);
        mine.Remediation.ShouldBe(theirs.Remediation);
    }

    [Theory]
    [InlineData("""{ "tools": [] }""")]
    [InlineData("""{ "tools": [], "freeSpace": [] }""")]
    public async Task ExecuteAsync_WithNoFreeSpaceEntries_IsNotApplicable(string manifest)
    {
        (await Run(manifest, ProbeReporting(9_000))).Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <summary>
    /// A host that offers no probe leaves the rule with nothing to say.
    /// </summary>
    /// <remarks>
    /// Not <c>Passed</c>, which would claim a measurement nobody took, and not
    /// <c>Errored</c>, which would call a host that predates the probe a defect.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithNoVolumeProbe_IsNotApplicable()
    {
        var outcome = await Run(Manifest(Entry(".")), probe: null);

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
        outcome.Status.ShouldNotBe(RuleStatus.Passed);
        outcome.Status.ShouldNotBe(RuleStatus.Errored);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheProbeAnswersNothing_DoesNotInventAVerdict()
    {
        var outcome = await Run(Manifest(Entry(".")), ProbeReporting([null]));

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <summary>
    /// How several paths fold into one status.
    /// </summary>
    /// <remarks>
    /// The row that is easiest to get wrong is the third: one path unmeasured
    /// and one comfortably above its floor. Reporting that as <c>Passed</c>
    /// would assert more than was measured, which is precisely what
    /// <c>NotApplicable</c> exists to avoid.
    /// </remarks>
    public static TheoryData<long?[], RuleStatus> Foldings() => new()
    {
        { [null], RuleStatus.NotApplicable },
        { [null, 500], RuleStatus.Failed },
        { [null, 9_000], RuleStatus.NotApplicable },
        { [9_000, 9_000], RuleStatus.Passed },
        { [500, 9_000], RuleStatus.Failed },
        { [1_000], RuleStatus.Passed },
    };

    [Theory]
    [MemberData(nameof(Foldings))]
    public async Task ExecuteAsync_FoldsThePerPathOutcomesIntoOneStatus(long?[] available, RuleStatus expected)
    {
        var entries = available.Select((_, index) => Entry($"path{index}")).ToArray();

        (await Run(Manifest(entries), ProbeReporting(available))).Status.ShouldBe(expected);
    }

    [Theory]
    [InlineData(999, RuleStatus.Failed)]
    [InlineData(1_000, RuleStatus.Passed)]
    [InlineData(1_001, RuleStatus.Passed)]
    public async Task ExecuteAsync_AppliesTheFloorAtItsBoundary(long available, RuleStatus expected)
    {
        (await Run(Manifest(Entry(".")), ProbeReporting(available))).Status.ShouldBe(expected);
    }

    /// <remarks>
    /// A probe that throws is a bad machine or an unreachable share, not a
    /// broken workspace. Reporting <c>Failed</c> would send the reader to clear
    /// disk space they have plenty of.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheProbeThrows_DoesNotReportTheWorkspaceAsFailed()
    {
        var probe = Substitute.For<IVolumeProbe>();

        probe.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<VolumeSpace?>>(_ => throw new IOException("the share is gone"));

        (await Run(Manifest(Entry(".")), probe)).Status.ShouldNotBe(RuleStatus.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_ProbesThePathRelativeToTheWorkspaceRoot()
    {
        var probe = ProbeReporting(9_000);

        await Run(Manifest(Entry("content")), probe);

        await probe.Received(1).ProbeAsync(
            Path.Combine(WorkspaceRoot.FullName, "content"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A floor larger than the whole volume gets a different sentence.
    /// </summary>
    /// <remarks>
    /// Telling somebody to free space they could never free is worse than
    /// saying nothing: they spend an afternoon deleting things before working
    /// out that the number itself is wrong.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheFloorExceedsTheVolume_SaysSoRatherThanTellingTheReaderToFreeSpace()
    {
        var outcome = await Run(Manifest(Entry(".", "1000000")), ProbeReporting(9_000));

        outcome.Status.ShouldBe(RuleStatus.Failed);

        var remediation = outcome.Findings.ShouldHaveSingleItem().Remediation.ShouldNotBeNull();

        remediation.ShouldNotContain("Free ");
        remediation.ShouldContain("larger than the volume");
    }

    [Fact]
    public async Task ExecuteAsync_ReportsFindingsInManifestOrder()
    {
        var manifest = Manifest(Entry("first"), Entry("second"), Entry("third"));

        var outcome = await Run(manifest, ProbeReporting(500, 9_000, 400));

        outcome.Findings.Select(finding => finding.Location!.RelativePath).ShouldBe(["first", "third"]);
    }

    /// <remarks>
    /// Once per declared path, even when two of them turn out to live on one
    /// volume. Deduplicating would need the rule to know which volume a path
    /// resolves to, which is the question it asked the probe in the first place.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithTwoEntriesOnOneVolume_ProbesOncePerDeclaredPath()
    {
        var probe = ProbeReporting(9_000);

        await Run(Manifest(Entry("a"), Entry("b")), probe);

        await probe.Received(2).ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <remarks>
    /// A floor of zero still measured the volume, and somebody wrote it down.
    /// Reporting n/a would erase the difference between a declaration and its
    /// absence.
    /// </remarks>
    [Theory]
    [InlineData("0")]
    [InlineData(null)]
    public async Task ExecuteAsync_WithAMinimumOfZero_Passes(string? minimum)
    {
        var entry = minimum is null
            ? """{ "path": "." }"""
            : Entry(".", minimum);

        (await Run(Manifest(entry), ProbeReporting(9_000))).Status.ShouldBe(RuleStatus.Passed);
    }

    /// <remarks>
    /// A deadline that expired inside the probe is the tool’s verdict to give.
    /// A rule that caught it would report the workspace as short of disk when
    /// what happened is that a dead network share took too long to answer —
    /// and the partial history record, which exists precisely for an interrupted
    /// run, would never be written.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenTheProbeIsCancelled_DoesNotSwallowIt()
    {
        var probe = Substitute.For<IVolumeProbe>();

        probe.ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<VolumeSpace?>>(_ => throw new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() =>
            Run(Manifest(Entry(".")), probe));
    }

    [Fact]
    public async Task ExecuteAsync_WithACancelledToken_StopsRatherThanFinishing()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _rule.ExecuteAsync(
                Context(
                    fileSystem: ManifestContaining(Manifest(Entry("."))),
                    volumes: ProbeReporting(9_000),
                    stage: ValidationStage.Workspace),
                cancellation.Token));
    }
}
