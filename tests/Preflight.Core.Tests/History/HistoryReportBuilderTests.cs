namespace Preflight.Core.Tests.History;

using Preflight.Abstractions;
using Preflight.Core.History;
using static Preflight.Core.Tests.History.HistoryEntries;

/// <summary>
/// The numbers of the report, and every exclusion
/// behind it.
/// </summary>
/// <remarks>
/// Each exclusion below exists because the obvious implementation produces a
/// number that is wrong in a way nobody would notice from the screen.
/// </remarks>
public sealed class HistoryReportBuilderTests
{
    /// <summary>
    /// The lower edge of the window is inclusive, and anything older is out.
    /// </summary>
    [Fact]
    public async Task BuildAsync_ForEventsAroundTheWindowEdge_IncludesTheEdge()
    {
        var report = await Build(
            TimeSpan.FromDays(30),
            Run(ago: TimeSpan.FromDays(29)),
            Run(ago: TimeSpan.FromDays(30)),
            Run(ago: TimeSpan.FromDays(30) + TimeSpan.FromSeconds(1)));

        report.RunCount.ShouldBe(2);
    }

    /// <remarks>
    /// The four counts cover every verdict aggregation produces, and they have
    /// to add up to the total. The report's example omits <c>Errored</c>, and
    /// a breakdown that does not close loses the reader on the first check.
    /// </remarks>
    [Fact]
    public async Task BuildAsync_CountsEveryVerdictAndTheyAddUp()
    {
        var report = await Build(
            TimeSpan.FromDays(30),
            Run(verdict: RunVerdict.Passed),
            Run(verdict: RunVerdict.Passed),
            Run(verdict: RunVerdict.PassedWithWarnings),
            Run(verdict: RunVerdict.Blocked),
            Run(verdict: RunVerdict.Errored));

        report.PassedCount.ShouldBe(2);
        report.PassedWithWarningsCount.ShouldBe(1);
        report.BlockedCount.ShouldBe(1);
        report.ErroredCount.ShouldBe(1);

        (report.PassedCount + report.PassedWithWarningsCount + report.BlockedCount + report.ErroredCount)
            .ShouldBe(report.RunCount);
    }

    /// <summary>
    /// A block is attributed to the stage that produced it.
    /// </summary>
    /// <remarks>
    /// Not decoration: an upper bound of build time not spent can only be built
    /// from blocks at build readiness. A pre-submit block saves a review round.
    /// </remarks>
    [Fact]
    public async Task BuildAsync_ForBlocksAtTwoStages_CountsThemApart()
    {
        var report = await Build(
            TimeSpan.FromDays(30),
            Run(verdict: RunVerdict.Blocked, stage: ValidationStage.BuildReadiness),
            Run(verdict: RunVerdict.Blocked, stage: ValidationStage.BuildReadiness),
            Run(verdict: RunVerdict.Blocked, stage: ValidationStage.PreSubmit));

        report.BlockingVerdicts.ShouldBe([
            new StageBlockCount(ValidationStage.PreSubmit, 1),
            new StageBlockCount(ValidationStage.BuildReadiness, 2),
        ]);
    }

    /// <summary>
    /// A warning promoted by <c>--fail-on-warning</c> is not a blocking verdict.
    /// </summary>
    /// <remarks>
    /// Verdict aggregation says confusing the two overstates what the tool caught, and
    /// the instrumentation exists in order not to make that mistake. The distinction is
    /// drawn from the executions: without a failure, the promotion is the only
    /// thing that could have blocked the run.
    /// </remarks>
    [Fact]
    public async Task BuildAsync_ForARunBlockedOnlyByFailOnWarning_DoesNotCountItAsABlockingVerdict()
    {
        var report = await Build(
            TimeSpan.FromDays(30),
            Run(
                verdict: RunVerdict.Blocked,
                failOnWarning: true,
                executions: [Execution("core.workspace.dependencies", RuleStatus.Warning, 0.2)]),
            Run(
                verdict: RunVerdict.Blocked,
                failOnWarning: true,
                executions: [Execution("core.build.configuration", RuleStatus.Failed, 0.6)]),
            Run(
                verdict: RunVerdict.Blocked,
                executions: [Execution("core.build.configuration", RuleStatus.Failed, 0.6)]));

        report.BlockedCount.ShouldBe(3);
        report.PromotedBlockCount.ShouldBe(1);
        report.BlockingVerdicts.ShouldHaveSingleItem()
            .ShouldBe(new StageBlockCount(ValidationStage.BuildReadiness, 2));
    }

    /// <remarks>
    /// <c>--no-skip</c> is recorded on every run for this line: a contrast
    /// run reports more failures by design, and a report that cannot see the
    /// flag inflates the failure count.
    /// </remarks>
    [Fact]
    public async Task BuildAsync_CountsContrastRuns()
    {
        var report = await Build(TimeSpan.FromDays(30), Run(noSkip: true), Run());

        report.ContrastRunCount.ShouldBe(1);
    }

    /// <summary>
    /// A cancelled run is counted and left out of the percentiles.
    /// </summary>
    /// <remarks>
    /// The concurrency contract records it so it is not invisible. Nothing there says its
    /// partial duration is a duration, and a run interrupted at three seconds
    /// enters the median as a three-second run.
    /// </remarks>
    [Fact]
    public async Task BuildAsync_ForACancelledRun_CountsItButKeepsItOutOfThePercentiles()
    {
        var report = await Build(
            TimeSpan.FromDays(30),
            [
                .. Repeat(Run(seconds: 20), 5),
                Run(seconds: 1, partial: true),
            ]);

        report.RunCount.ShouldBe(6);
        report.PartialRunCount.ShouldBe(1);
        report.PreflightDuration.SampleSize.ShouldBe(5);
        report.PreflightDuration.P50.ShouldBe(TimeSpan.FromSeconds(20));
    }

    /// <summary>
    /// A skip is not a fast rule.
    /// </summary>
    /// <remarks>
    /// A skipped execution costs no time, and a zero next to a real measurement
    /// does not average with it — it replaces the ranking with one about how
    /// often a rule was skipped, which inverts the answer for exactly the rule
    /// the report's example puts at the top.
    /// </remarks>
    [Fact]
    public async Task BuildAsync_ForSkippedAndNotApplicableExecutions_LeavesThemOutOfTheSlowestRules()
    {
        var report = await Build(
            TimeSpan.FromDays(30),
            [
                .. Repeat(
                    Run(executions:
                    [
                        Execution("core.build.compile-probe", RuleStatus.Passed, 14.9),
                        Execution("core.workspace.dependencies", RuleStatus.Passed, 2.1),
                    ]),
                    5),
                .. Repeat(
                    Run(executions:
                    [
                        Execution("core.build.compile-probe", RuleStatus.Skipped, 0),
                        Execution("core.workspace.dependencies", RuleStatus.NotApplicable, 0),
                    ]),
                    20),
            ]);

        report.SlowestRules.ShouldBe([
            new RuleDuration("core.build.compile-probe", TimeSpan.FromSeconds(14.9)),
            new RuleDuration("core.workspace.dependencies", TimeSpan.FromSeconds(2.1)),
        ]);
    }

    /// <summary>
    /// A cancelled run's rule durations are excluded too.
    /// </summary>
    /// <remarks>
    /// For its own reason, not the skip one: the record says which rules ran and
    /// not which one the cancellation interrupted, so a rule that was mid-flight
    /// contributes a truncated duration indistinguishable from a fast one. The
    /// failure counts are still taken, because a rule that failed before the
    /// cancellation did fail.
    /// </remarks>
    [Fact]
    public async Task BuildAsync_ForACancelledRun_LeavesItsExecutionsOutOfTheSlowestRules()
    {
        var report = await Build(
            TimeSpan.FromDays(30),
            [
                .. Repeat(
                    Run(executions: [Execution("core.build.compile-probe", RuleStatus.Passed, 14.9)]),
                    5),
                .. Repeat(
                    Run(
                        partial: true,
                        executions: [Execution("core.build.compile-probe", RuleStatus.Failed, 0.1)]),
                    20),
            ]);

        report.SlowestRules.ShouldHaveSingleItem()
            .ShouldBe(new RuleDuration("core.build.compile-probe", TimeSpan.FromSeconds(14.9)));

        report.MostFrequentFailures.ShouldHaveSingleItem()
            .ShouldBe(new RuleFailureCount("core.build.compile-probe", 20));
    }

    /// <summary>
    /// A cached execution is a lookup, and stays out of the duration ranking.
    /// </summary>
    /// <remarks>
    /// The cache makes the expensive rule look cheap in exact proportion to how
    /// well it caches. Counting the hits would take the measurement that was
    /// named as its own revisit trigger and destroy it with the thing the
    /// trigger was for. Failure counts still include them: a cached failure is
    /// still a rule that failed.
    /// </remarks>
    [Fact]
    public async Task BuildAsync_ForACachedExecution_LeavesItOutOfTheSlowestRules()
    {
        var report = await Build(
            TimeSpan.FromDays(30),
            [
                .. Repeat(
                    Run(executions: [Execution("core.build.compile-probe", RuleStatus.Passed, 14.9)]),
                    5),
                .. Repeat(
                    Run(executions:
                    [
                        Execution("core.build.compile-probe", RuleStatus.Passed, 0) with { FromCache = true },
                    ]),
                    50),
            ]);

        report.SlowestRules.ShouldHaveSingleItem()
            .ShouldBe(new RuleDuration("core.build.compile-probe", TimeSpan.FromSeconds(14.9)));
    }

    [Fact]
    public async Task BuildAsync_ForACachedFailure_StillCountsItAsAFailure()
    {
        var report = await Build(
            TimeSpan.FromDays(30),
            Run(executions:
            [
                Execution("core.build.configuration", RuleStatus.Failed, 0) with { FromCache = true },
            ]));

        report.MostFrequentFailures.ShouldHaveSingleItem()
            .ShouldBe(new RuleFailureCount("core.build.configuration", 1));
    }

    /// <remarks>
    /// Ordinal by rule id when two medians tie. Left to a dictionary's order the
    /// report would stop being diffable, which the determinism guarantee does not allow.
    /// </remarks>
    [Fact]
    public async Task BuildAsync_ForTwoRulesWithTheSameMedian_OrdersByRuleIdOrdinal()
    {
        var report = await Build(
            TimeSpan.FromDays(30),
            [
                .. Repeat(
                    Run(executions:
                    [
                        Execution("core.zeta", RuleStatus.Passed, 1),
                        Execution("core.alpha", RuleStatus.Passed, 1),
                    ]),
                    5),
            ]);

        report.SlowestRules.Select(rule => rule.RuleId).ShouldBe(["core.alpha", "core.zeta"]);
    }

    /// <remarks>
    /// A ranking that silently stops at five reads as "these are all of them".
    /// </remarks>
    [Fact]
    public async Task BuildAsync_WithMoreRulesThanTheRankingShows_SaysHowManyAreNotShown()
    {
        var executions = Enumerable.Range(0, 8)
            .Select(index => Execution($"core.rule-{index}", RuleStatus.Failed, index + 1))
            .ToArray();

        var report = await Build(TimeSpan.FromDays(30), [.. Repeat(Run(executions: executions), 5)]);

        report.SlowestRules.Count.ShouldBe(HistoryReportBuilder.TopRuleCount);
        report.SlowestRulesNotShown.ShouldBe(3);
        report.MostFrequentFailures.Count.ShouldBe(HistoryReportBuilder.TopRuleCount);
        report.MostFrequentFailuresNotShown.ShouldBe(3);
    }

    /// <remarks>
    /// Both statuses count as a failure: the exit-code contract separates a rule that
    /// rejected the code from a rule that broke, and a report about how often a
    /// rule stops a run needs both.
    /// </remarks>
    [Fact]
    public async Task BuildAsync_CountsFailedAndErroredExecutionsAsFailures()
    {
        var report = await Build(
            TimeSpan.FromDays(30),
            Run(executions:
            [
                Execution("core.workspace.toolchain", RuleStatus.Failed, 1),
                Execution("core.build.configuration", RuleStatus.Errored, 1),
                Execution("core.presubmit.large-file", RuleStatus.Passed, 1),
            ]),
            Run(executions: [Execution("core.workspace.toolchain", RuleStatus.Failed, 1)]));

        report.MostFrequentFailures.ShouldBe([
            new RuleFailureCount("core.workspace.toolchain", 2),
            new RuleFailureCount("core.build.configuration", 1),
        ]);
    }

    [Fact]
    public async Task BuildAsync_GroupsMeasurementsByLabelOrdinally()
    {
        var report = await Build(
            TimeSpan.FromDays(30),
            External("package", 60),
            External("build", 2280),
            External("build", 2280));

        report.Measured.Select(series => series.Label).ShouldBe(["build", "package"]);
        report.Measured[0].Duration.SampleSize.ShouldBe(2);
    }

    /// <summary>
    /// The upper bound multiplies build-readiness blocks by the median build.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WithBlocksAndAMedianBuild_ComputesTheUpperBound()
    {
        var report = await Build(
            TimeSpan.FromDays(30),
            [
                .. Repeat(External("build", 60), 5),
                .. Repeat(Run(verdict: RunVerdict.Blocked, stage: ValidationStage.BuildReadiness), 3),
                Run(verdict: RunVerdict.Blocked, stage: ValidationStage.PreSubmit),
            ]);

        report.UpperBoundNotSpent.ShouldBe(TimeSpan.FromMinutes(3));
    }

    /// <remarks>
    /// without a median to multiply, there is no ceiling — and zero is
    /// a claim, not an absence.
    /// </remarks>
    [Theory]
    [InlineData(4)]
    [InlineData(0)]
    public async Task BuildAsync_WithoutEnoughMeasuredBuilds_HasNoUpperBound(int builds)
    {
        var report = await Build(
            TimeSpan.FromDays(30),
            [
                .. Repeat(External("build", 60), builds),
                Run(verdict: RunVerdict.Blocked, stage: ValidationStage.BuildReadiness),
            ]);

        report.UpperBoundNotSpent.ShouldBeNull();
    }

    /// <remarks>
    /// Measured under a different label, the series exists and the ceiling still
    /// does not: the report builds it out of build time, and "package" is not
    /// build time.
    /// </remarks>
    [Fact]
    public async Task BuildAsync_WithMeasurementsUnderAnotherLabel_HasNoUpperBound()
    {
        var report = await Build(
            TimeSpan.FromDays(30),
            [
                .. Repeat(External("package", 60), 5),
                Run(verdict: RunVerdict.Blocked, stage: ValidationStage.BuildReadiness),
            ]);

        report.UpperBoundNotSpent.ShouldBeNull();
    }

    /// <summary>
    /// Lines nobody can read are counted whatever the window is.
    /// </summary>
    /// <remarks>
    /// They carry no instant to compare against, and dropping them silently is
    /// what this refuses: percentiles over an unknown fraction of the sample.
    /// </remarks>
    [Fact]
    public async Task BuildAsync_CountsUnreadableAndIgnoredLines()
    {
        var report = await Build(
            TimeSpan.FromDays(30),
            new HistoryEntry.Unreadable("2026-08.WKS-1234.ndjson", 4),
            new HistoryEntry.Unreadable("2026-08.WKS-1234.ndjson", 9),
            new HistoryEntry.Ignored("telepathy"),
            Run());

        report.UnreadableLineCount.ShouldBe(2);
        report.IgnoredLineCount.ShouldBe(1);
        report.RunCount.ShouldBe(1);
    }

    [Fact]
    public async Task BuildAsync_OverAnEmptyHistory_ReportsNothingRatherThanZeroes()
    {
        var report = await Build(TimeSpan.FromDays(30));

        report.RunCount.ShouldBe(0);
        report.Measured.ShouldBeEmpty();
        report.SlowestRules.ShouldBeEmpty();
        report.MostFrequentFailures.ShouldBeEmpty();
        report.PreflightDuration.ShouldBe(DurationSummary.Empty);
        report.UpperBoundNotSpent.ShouldBeNull();
    }

    private static Task<HistoryReport> Build(TimeSpan window, params HistoryEntry[] entries) =>
        Build(window, (IEnumerable<HistoryEntry>)entries);

    private static Task<HistoryReport> Build(TimeSpan window, IEnumerable<HistoryEntry> entries) =>
        HistoryReportBuilder.BuildAsync(
            Stream(entries),
            Now,
            window,
            TestContext.Current.CancellationToken);

    private static async IAsyncEnumerable<HistoryEntry> Stream(IEnumerable<HistoryEntry> entries)
    {
        foreach (var entry in entries)
        {
            await Task.Yield();

            yield return entry;
        }
    }
}
