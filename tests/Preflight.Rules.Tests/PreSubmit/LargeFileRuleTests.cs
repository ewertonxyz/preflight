namespace Preflight.Rules.Tests.PreSubmit;

using NSubstitute;
using Preflight.Abstractions.Model;
using Preflight.Abstractions.Rules;
using Preflight.Abstractions.Services;
using Preflight.Rules;
using static Preflight.Rules.Tests.RuleFixture;

/// <summary>
/// Fixes <see cref="LargeFileRule"/>, the rule that makes <c>NotApplicable</c>
/// worth having.
/// </summary>
/// <remarks>
/// No disk is touched. The unit layer runs against substituted services
/// precisely so a rule can be exercised at a size boundary without a
/// five-megabyte file in the repository.
/// </remarks>
public sealed class LargeFileRuleTests
{
    private const long Limit = 1024;

    private readonly LargeFileRule _rule = new();

    private static IFileSystem FileSystemSizing(params (string Path, long Size)[] files)
    {
        var fileSystem = Substitute.For<IFileSystem>();

        foreach (var (path, size) in files)
        {
            fileSystem.GetFileSize(Arg.Is<string>(candidate => candidate.EndsWith(path, StringComparison.Ordinal)))
                .Returns(size);
        }

        return fileSystem;
    }

    private Task<RuleOutcome> Run(
        IReadOnlyList<ChangedFile> changed,
        IFileSystem fileSystem,
        long maxBytes = Limit) =>
        _rule.ExecuteAsync(
            Context(changed, PolicyWith("maxBytes", maxBytes), fileSystem),
            CancellationToken.None);

    /// <remarks>
    /// A commit touching only <c>.md</c> files makes this report <c>n/a</c>
    /// rather than a tick, because it measured nothing and a tick would claim
    /// more than is known.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithNoChangedFiles_IsNotApplicable()
    {
        var outcome = await Run([], FileSystemSizing());

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <summary>
    /// A commit that only deletes files examined nothing.
    /// </summary>
    /// <remarks>
    /// The distinction a naive implementation loses. Filtering deletions at the
    /// size call rather than before the count would report <c>Passed</c> here —
    /// a claim that files were measured and found small, about files that no
    /// longer exist.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithOnlyDeletions_IsNotApplicable()
    {
        var outcome = await Run([Deleted("art/huge.bin")], FileSystemSizing());

        outcome.Status.ShouldBe(RuleStatus.NotApplicable);
    }

    /// <remarks>
    /// Measuring a deleted file throws, and an exception out of a rule is
    /// <c>Errored</c> — the tool blaming itself for a perfectly ordinary
    /// commit.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithADeletion_NeverMeasuresIt()
    {
        var fileSystem = FileSystemSizing();

        await Run([Deleted("art/gone.bin"), Added("src/a.cs")], fileSystem);

        fileSystem.DidNotReceive().GetFileSize(Arg.Is<string>(path => path.Contains("gone.bin")));
    }

    /// <remarks>
    /// "Exceeds" is <c>&gt;</c>, not <c>&gt;=</c>. A file of exactly the limit
    /// is within it, and the off-by-one in the other direction fails a commit
    /// that obeys the policy to the byte.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_AtExactlyTheLimit_Passes()
    {
        var outcome = await Run([Added("art/exact.bin")], FileSystemSizing(("exact.bin", Limit)));

        outcome.Status.ShouldBe(RuleStatus.Passed);
    }

    [Fact]
    public async Task ExecuteAsync_OneByteOverTheLimit_Fails()
    {
        var outcome = await Run([Added("art/over.bin")], FileSystemSizing(("over.bin", Limit + 1)));

        outcome.Status.ShouldBe(RuleStatus.Failed);
        outcome.Findings.ShouldHaveSingleItem();
    }

    /// <remarks>
    /// A rule that fails without saying how to fix it delivers half the work:
    /// the reader now knows something is wrong and still has to work out what
    /// to do about it. Asserted here per rule, and again across every rule and
    /// every broken fixture in the integration layer, because the way it
    /// regresses is a finding added to an existing rule rather than a new rule
    /// arriving without one.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WhenItFails_ReportsExpectedActualAndARemedy()
    {
        var outcome = await Run([Added("art/over.bin")], FileSystemSizing(("over.bin", 4096)));

        var finding = outcome.Findings.ShouldHaveSingleItem();

        finding.Location!.RelativePath.ShouldBe("art/over.bin");
        finding.Expected.ShouldNotBeNull().ShouldContain("1,024");
        finding.Actual.ShouldNotBeNull().ShouldContain("4,096");
        finding.Remediation.ShouldNotBeNullOrWhiteSpace();
    }

    /// <remarks>
    /// A rename's <c>PreviousRelativePath</c> names a file that no longer
    /// exists. Measuring it throws, and the rule turns <c>Errored</c> on a
    /// commit that did nothing unusual.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_ForARename_MeasuresTheNewPath()
    {
        var fileSystem = FileSystemSizing(("new.bin", 10));

        await Run([Renamed("art/old.bin", "art/new.bin")], fileSystem);

        fileSystem.Received().GetFileSize(Arg.Is<string>(path => path.EndsWith("new.bin", StringComparison.Ordinal)));
        fileSystem.DidNotReceive().GetFileSize(Arg.Is<string>(path => path.Contains("old.bin")));
    }

    /// <summary>
    /// The limit comes from policy, not from a constant.
    /// </summary>
    /// <remarks>
    /// The same file, two policies, opposite verdicts. A rule with the limit
    /// baked in passes every other test in this class.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_ReadsTheLimitFromPolicy()
    {
        var fileSystem = FileSystemSizing(("asset.bin", 2048));

        (await Run([Added("art/asset.bin")], fileSystem, maxBytes: 1024)).Status.ShouldBe(RuleStatus.Failed);
        (await Run([Added("art/asset.bin")], fileSystem, maxBytes: 4096)).Status.ShouldBe(RuleStatus.Passed);
    }

    /// <remarks>
    /// A production shipping large assets raises the limit, and the usual
    /// raise — 50 MB, or 52 428 800 bytes — still fits in an
    /// <see cref="int"/>, so it would pass even if the limit were read as one.
    /// This test uses a limit above <see cref="int.MaxValue"/>, which is where
    /// reading it as an <see cref="int"/> overflows and the rule turns
    /// <c>Errored</c> in the middle of a run.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithALimitAboveIntMaxValue_StillCompares()
    {
        var limit = (long)int.MaxValue + 1024;
        var fileSystem = FileSystemSizing(("huge.bin", limit - 1));

        (await Run([Added("art/huge.bin")], fileSystem, limit)).Status.ShouldBe(RuleStatus.Passed);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoPolicy_UsesTheDefaultLimit()
    {
        var fileSystem = FileSystemSizing(("asset.bin", LargeFileRule.DefaultMaxBytes + 1));

        var outcome = await _rule.ExecuteAsync(
            Context([Added("art/asset.bin")], EmptyPolicy(), fileSystem),
            CancellationToken.None);

        outcome.Status.ShouldBe(RuleStatus.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsOneFindingPerOversizedFile_InTheOrderGiven()
    {
        var fileSystem = FileSystemSizing(("a.bin", 5000), ("b.bin", 10), ("c.bin", 9000));

        var outcome = await Run(
            [Added("art/a.bin"), Added("art/b.bin"), Added("art/c.bin")],
            fileSystem);

        outcome.Findings.Select(finding => finding.Location!.RelativePath)
            .ShouldBe(["art/a.bin", "art/c.bin"]);
    }

    /// <remarks>
    /// Checking the token inside the loop is what a rule author owes the
    /// tool. A pre-submit rule can receive tens of thousands of entries, and
    /// one that never checks cannot be stopped: the timeout fires, the tool
    /// records the run as over, and the thread keeps measuring files nobody is
    /// waiting for.
    /// </remarks>
    [Fact]
    public async Task ExecuteAsync_WithACancelledToken_StopsRatherThanFinishing()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var changed = Enumerable.Range(0, 5_000)
            .Select(index => Added($"art/asset-{index}.bin"))
            .ToArray();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _rule.ExecuteAsync(
                Context(changed, PolicyWith("maxBytes", Limit), FileSystemSizing()),
                cancellation.Token));
    }
}
